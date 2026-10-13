using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Channeled.Application.Teams;
using Channeled.Domain.Audit;
using Channeled.Domain.Errors;
using Channeled.Domain.Identity;
using Channeled.Domain.Teams;
using Channeled.Infrastructure.PublicLinks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Channeled.Tests.Api;

/// <summary>Team privacy rules over HTTP: settings validation, grants vs. roles, role-change side effects, share links.</summary>
[Collection("Api")]
public class TeamPrivacyAuthzTests(AuthApiFactory factory) : TeamPrivacyTestBase(factory), IClassFixture<AuthApiFactory>
{
    // ------------------------------------------------------------ 1. default join role

    [Theory]
    [InlineData(MemberRole.Admin)]
    [InlineData(MemberRole.Owner)]
    public async Task Default_join_role_can_never_be_set_above_member_even_by_an_admin(MemberRole requested)
    {
        var admin = await NewMemberAsync("Adam", MemberRole.Admin);

        var byAdmin = await admin.Client.PutAsJsonAsync(TeamUrl("/settings"), new UpdateTeamSettingsRequest(null, null, null, requested));
        byAdmin.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorCodeAsync(byAdmin)).Should().Be(ErrorCodes.Validation);

        var byOwner = await Owner.Client.PutAsJsonAsync(TeamUrl("/settings"), new UpdateTeamSettingsRequest(null, null, null, requested));
        byOwner.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // A valid value is still Owner-only: Admins are forbidden (not silently ignored).
        var valid = await admin.Client.PutAsJsonAsync(TeamUrl("/settings"), new UpdateTeamSettingsRequest(null, null, null, MemberRole.Member));
        valid.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await admin.Client.PutAsJsonAsync(TeamUrl("/settings"), new UpdateTeamSettingsRequest(null, null, TeamJoinMode.OpenJoin, null, true)))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var team = (await Owner.Client.GetFromJsonAsync<TeamDto>(TeamUrl()))!;
        team.DefaultJoinRole.Should().Be(MemberRole.Viewer);
        team.JoinMode.Should().Be(TeamJoinMode.RequestToJoin);
    }

    // ------------------------------------------------------------ 4. grants vs. team role

    [Fact]
    public async Task Restricted_write_needs_member_role_and_a_writable_grant_and_role_changes_apply_immediately()
    {
        await CreateChannelAsync("vip", ChannelVisibility.Restricted, ChannelKind.Text);
        var vera = await NewMemberAsync("Vera", MemberRole.Viewer);
        var rick = await NewMemberAsync("Rick", MemberRole.Viewer);
        var mia = await NewMemberAsync("Mia", MemberRole.Member);
        await GrantAsync("vip", vera, readOnly: false);
        await GrantAsync("vip", rick, readOnly: true);
        await GrantAsync("vip", mia, readOnly: false);

        // Viewer + writable grant: can read, cannot write. Member + writable grant: writes.
        (await GetChannelAsync(vera, "vip")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PostMessageAsync(vera, "vip")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await PostMessageAsync(mia, "vip")).StatusCode.Should().Be(HttpStatusCode.Created);

        // Viewer → Member: the writable grant takes effect on the very next request (no stale authorization).
        await SetRoleAsync(vera, MemberRole.Member);
        (await PostMessageAsync(vera, "vip")).StatusCode.Should().Be(HttpStatusCode.Created);

        // ... unless the grant is ReadOnly: promotion alone never grants write.
        await SetRoleAsync(rick, MemberRole.Member);
        (await GetChannelAsync(rick, "vip")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PostMessageAsync(rick, "vip")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await GrantAsync("vip", rick, readOnly: false);
        (await PostMessageAsync(rick, "vip")).StatusCode.Should().Be(HttpStatusCode.Created);

        // Demotion back to Viewer removes write immediately, grant untouched.
        await SetRoleAsync(vera, MemberRole.Viewer);
        (await PostMessageAsync(vera, "vip")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await GetChannelAsync(vera, "vip")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Readonly_grant_is_dormant_while_everyone_and_binds_once_the_channel_is_restricted()
    {
        await CreateChannelAsync("lounge", ChannelVisibility.Everyone, ChannelKind.Text);
        var mia = await NewMemberAsync("Mia", MemberRole.Member);
        await GrantAsync("lounge", mia, readOnly: true);

        (await PostMessageAsync(mia, "lounge")).StatusCode.Should().Be(HttpStatusCode.Created);

        var restricted = await SetVisibilityAsync("lounge", ChannelVisibility.Restricted);
        restricted.MembersLosingAccess.Should().Be(0, "Mia keeps access through her (read-only) grant");
        (await GetChannelAsync(mia, "lounge")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PostMessageAsync(mia, "lounge")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await SetVisibilityAsync("lounge", ChannelVisibility.Everyone);
        (await PostMessageAsync(mia, "lounge")).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // ------------------------------------------------------------ 8. role changes

    [Fact]
    public async Task Admin_promotion_deletes_redundant_grants_and_demotion_audits_lost_restricted_channels()
    {
        var alice = await NewMemberAsync("Alice", MemberRole.Member);
        await CreateChannelAsync("r1", ChannelVisibility.Restricted);
        await CreateChannelAsync("r2", ChannelVisibility.Restricted);
        await CreateChannelAsync("e1", ChannelVisibility.Everyone);
        await GrantAsync("r2", alice, readOnly: false);
        await GrantAsync("e1", alice, readOnly: true); // dormant
        (await GetChannelAsync(alice, "r1")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Promotion: Admin access is implicit, so every explicit grant (incl. dormant ones) is dropped.
        await SetRoleAsync(alice, MemberRole.Admin);
        (await DbAsync(db => db.ChannelMembers.CountAsync(m => m.UserId == alice.Id))).Should().Be(0);
        (await LostChannelsInLatestRoleChangeAsync(alice.Id)).Should().BeEmpty();
        (await GetChannelAsync(alice, "r1")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetChannelAsync(alice, "r2")).StatusCode.Should().Be(HttpStatusCode.OK);

        // Demotion: the audit lists exactly the Restricted channels that were only visible through the Admin role.
        await GrantAsync("r2", alice, readOnly: false);
        await SetRoleAsync(alice, MemberRole.Member);
        (await LostChannelsInLatestRoleChangeAsync(alice.Id)).Should().Equal("r1");
        (await GetChannelAsync(alice, "r1")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await GetChannelAsync(alice, "r2")).StatusCode.Should().Be(HttpStatusCode.OK);

        var bob = await NewMemberAsync("Bob", MemberRole.Admin);
        await SetRoleAsync(bob, MemberRole.Member);
        (await LostChannelsInLatestRoleChangeAsync(bob.Id)).Should().Equal("r1", "r2");
        (await GetChannelAsync(bob, "r1")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await GetChannelAsync(bob, "r2")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ------------------------------------------------------------ 9. share links on restriction

    [Fact]
    public async Task Restricting_a_channel_revokes_only_links_of_members_who_lose_access_and_reports_the_count()
    {
        var docs = await CreateChannelAsync("docs", ChannelVisibility.Everyone);
        var other = await CreateChannelAsync("other", ChannelVisibility.Everyone);
        var alice = await NewMemberAsync("Alice", MemberRole.Member); // loses access
        var dave = await NewMemberAsync("Dave", MemberRole.Member); // loses access
        var bob = await NewMemberAsync("Bob", MemberRole.Member); // keeps access via grant
        var carol = await NewMemberAsync("Carol", MemberRole.Admin); // implicit access
        await GrantAsync("docs", bob, readOnly: true);

        var f1 = await SeedFileAsync(docs.Id, "f1.txt", alice.Id);
        var f2 = await SeedFileAsync(docs.Id, "f2.txt", alice.Id);
        var fOther = await SeedFileAsync(other.Id, "other.txt", alice.Id);

        var a1 = await SeedLinkAsync(f1.Id, docs.Id, alice.Id);
        var a2 = await SeedLinkAsync(f2.Id, docs.Id, alice.Id);
        var d1 = await SeedLinkAsync(f1.Id, docs.Id, dave.Id);
        var b1 = await SeedLinkAsync(f1.Id, docs.Id, bob.Id);
        var c1 = await SeedLinkAsync(f1.Id, docs.Id, carol.Id);
        var o1 = await SeedLinkAsync(f1.Id, docs.Id, Owner.Id);
        var alreadyRevoked = await SeedLinkAsync(f1.Id, docs.Id, alice.Id, revokedReason: ShareLinkRevoker.ReasonManual);
        var elsewhere = await SeedLinkAsync(fOther.Id, other.Id, alice.Id);

        var result = await SetVisibilityAsync("docs", ChannelVisibility.Restricted);
        result.MembersLosingAccess.Should().Be(2);
        result.ShareLinksRevoked.Should().Be(3, "only the live links of Alice (2) and Dave (1)");

        var links = await DbAsync(db => db.FileShareLinks.AsNoTracking().ToDictionaryAsync(l => l.Id));
        foreach (var revoked in new[] { a1, a2, d1 })
        {
            links[revoked].RevokedAt.Should().NotBeNull();
            links[revoked].RevokeReason.Should().Be(ShareLinkRevoker.ReasonChannelRestricted);
            links[revoked].RevokedBy.Should().Be(Owner.Id);
        }

        foreach (var alive in new[] { b1, c1, o1, elsewhere })
        {
            links[alive].RevokedAt.Should().BeNull();
        }

        links[alreadyRevoked].RevokeReason.Should().Be(ShareLinkRevoker.ReasonManual, "earlier revocations keep their original reason");

        // Not reversible by flipping back, and a second restriction has nothing left to revoke.
        await SetVisibilityAsync("docs", ChannelVisibility.Everyone);
        (await DbAsync(db => db.FileShareLinks.CountAsync(l => l.RevokedAt != null))).Should().Be(4);
        var again = await SetVisibilityAsync("docs", ChannelVisibility.Restricted);
        again.ShareLinksRevoked.Should().Be(0);
        (await DbAsync(db => db.FileShareLinks.AnyAsync(l => l.Id == b1 && l.RevokedAt == null))).Should().BeTrue();
    }

    // ------------------------------------------------------------ helpers

    private async Task<List<string>> LostChannelsInLatestRoleChangeAsync(Guid userId)
    {
        var target = userId.ToString();
        var json = await DbAsync(db => db.AuditEvents.AsNoTracking()
            .Where(a => a.TeamId == TeamId && a.EventType == AuditEventType.RoleChange && a.TargetId == target)
            .OrderByDescending(a => a.OccurredAt).ThenByDescending(a => a.Id)
            .Select(a => a.DetailsJson)
            .FirstAsync());
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("channelsLosingAccess").EnumerateArray()
            .Select(c => c.GetProperty("name").GetString()!)
            .ToList();
    }

    private async Task<Guid> SeedLinkAsync(Guid fileId, Guid channelId, Guid createdBy, string? revokedReason = null)
    {
        var now = DateTimeOffset.UtcNow;
        var link = new FileShareLinkEntity
        {
            Id = UuidV7.NewGuid(),
            TokenHash = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
            FileId = fileId,
            ChannelId = channelId,
            OriginalChannelId = channelId,
            TeamId = TeamId,
            FileName = "seed.txt",
            CreatedBy = createdBy,
            CreatedAt = now,
            ExpiresAt = now.AddDays(7),
            RevokedAt = revokedReason is null ? null : now,
            RevokedBy = revokedReason is null ? null : Owner.Id,
            RevokeReason = revokedReason,
        };
        await DbAsync(async db =>
        {
            db.FileShareLinks.Add(link);
            await db.SaveChangesAsync();
        });
        return link.Id;
    }
}
