using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Channeled.Application.Identity;
using Channeled.Application.Notifications;
using Channeled.Application.Search;
using Channeled.Application.Teams;
using Channeled.Domain.Audit;
using Channeled.Domain.Identity;
using Channeled.Domain.Notifications;
using Channeled.Domain.Teams;
using Channeled.Infrastructure.Files;
using Channeled.Infrastructure.Identity;
using Channeled.Infrastructure.Persistence;
using Channeled.Infrastructure.Security;
using Channeled.Infrastructure.Teams;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Channeled.Tests.Api;

/// <summary>Team join link, join requests, leave, role/visibility side effects and retired endpoints.</summary>
[Collection("Api")]
public class TeamJoinTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private HttpClient _owner = null!;
    private HttpClient _alice = null!;
    private HttpClient _bob = null!;
    private Guid _aliceId;
    private Guid _bobId;
    private string _slug = "";
    private string _token = "";

    public TeamJoinTests(AuthApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.ResetDatabaseAsync();
        await using var scope = _factory.Services.CreateAsyncScope();
        var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
        await auth.SeedOperatorAsync(new SeedOperatorRequest("owner@example.com", "Owner", "OwnerPassword1!"));
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        _aliceId = await CreateUserAsync(users, "alice@example.com", "Alice");
        _bobId = await CreateUserAsync(users, "bob@example.com", "Bob");

        _owner = await LoginAsync("owner@example.com", "OwnerPassword1!");
        _alice = await LoginAsync("alice@example.com", "UserPassword1!");
        _bob = await LoginAsync("bob@example.com", "UserPassword1!");

        var team = await (await _owner.PostAsJsonAsync("/api/v1/teams/", new CreateTeamRequest("JoinTeam", null)))
            .Content.ReadFromJsonAsync<TeamDto>();
        _slug = team!.Slug;
        team.JoinMode.Should().Be(TeamJoinMode.RequestToJoin);
        team.DefaultJoinRole.Should().Be(MemberRole.Viewer);

        var link = await _owner.GetFromJsonAsync<TeamJoinLinkDto>($"/api/v1/teams/{_slug}/join-link");
        _token = link!.Token;
        link.JoinPath.Should().Be($"/join/{_token}");
        link.Prefix.Should().Be(_token[..8]);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Retired_invitation_and_channel_join_endpoints_return_410()
    {
        (await _owner.PostAsJsonAsync($"/api/v1/teams/{_slug}/invitations", new CreateTeamInvitationRequest("x@example.com", MemberRole.Member)))
            .StatusCode.Should().Be(HttpStatusCode.Gone);
        (await _alice.PostAsJsonAsync("/api/v1/teams/invitations/accept", new AcceptTeamInvitationRequest("t")))
            .StatusCode.Should().Be(HttpStatusCode.Gone);
        (await _owner.GetAsync($"/api/v1/teams/{_slug}/channels/any/join")).StatusCode.Should().Be(HttpStatusCode.Gone);
        (await _owner.PostAsync($"/api/v1/teams/{_slug}/channels/any/join-requests", null)).StatusCode.Should().Be(HttpStatusCode.Gone);
    }

    [Fact]
    public async Task Anonymous_info_exposes_only_name_and_mode_and_unknown_token_is_generic()
    {
        using var anon = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var ok = await anon.GetAsync($"/api/v1/teams/join/{_token}");
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        ok.Headers.GetValues("Referrer-Policy").Should().Contain("no-referrer");
        var info = await ok.Content.ReadFromJsonAsync<TeamJoinInfoDto>();
        info!.TeamName.Should().Be("JoinTeam");
        info.IsMember.Should().BeFalse();
        info.PendingRequestId.Should().BeNull();

        var bad = await anon.GetAsync("/api/v1/teams/join/not-a-real-token");
        bad.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await bad.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("detail").GetString().Should().Be("Join link is invalid or expired.");
    }

    [Fact]
    public async Task Request_to_join_notifies_admins_and_approval_grants_chosen_role()
    {
        var join = await _alice.PostAsync($"/api/v1/teams/join/{_token}", null);
        join.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await join.Content.ReadFromJsonAsync<TeamJoinResultDto>();
        result!.Joined.Should().BeFalse();
        result.PendingRequestId.Should().NotBeNull();

        // Idempotent while pending.
        var again = await (await _alice.PostAsync($"/api/v1/teams/join/{_token}", null)).Content.ReadFromJsonAsync<TeamJoinResultDto>();
        again!.PendingRequestId.Should().Be(result.PendingRequestId);

        var info = await _alice.GetFromJsonAsync<TeamJoinInfoDto>($"/api/v1/teams/join/{_token}");
        info!.PendingRequestId.Should().Be(result.PendingRequestId);

        var count = await _owner.GetFromJsonAsync<PendingJoinCountDto>("/api/v1/notifications/pending-join-count");
        count!.Count.Should().Be(1);
        (await _alice.GetFromJsonAsync<PendingJoinCountDto>("/api/v1/teams/pending-join-count"))!.Count.Should().Be(0);

        var notes = await _owner.GetFromJsonAsync<NotificationListResponse>("/api/v1/notifications/?unreadOnly=true");
        notes!.Items.Should().ContainSingle(n => n.Type == NotificationType.TeamJoinRequest);

        var pending = await _owner.GetFromJsonAsync<List<TeamJoinRequestDto>>($"/api/v1/teams/{_slug}/join-requests");
        pending!.Should().ContainSingle(r => r.UserId == _aliceId);

        // Admin+ only roles Viewer|Member.
        (await _owner.PostAsJsonAsync($"/api/v1/teams/{_slug}/join-requests/{result.PendingRequestId}/approve", new ApproveTeamJoinRequestRequest(MemberRole.Admin)))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await _owner.PostAsJsonAsync($"/api/v1/teams/{_slug}/join-requests/{result.PendingRequestId}/approve", new ApproveTeamJoinRequestRequest(MemberRole.Member)))
            .EnsureSuccessStatusCode();

        var members = await _owner.GetFromJsonAsync<List<TeamMemberDto>>($"/api/v1/teams/{_slug}/members");
        members!.Single(m => m.UserId == _aliceId).Role.Should().Be(MemberRole.Member);

        // Notifications cleared, count drops, member re-join is a no-op success.
        (await _owner.GetFromJsonAsync<PendingJoinCountDto>("/api/v1/notifications/pending-join-count"))!.Count.Should().Be(0);
        var after = await _owner.GetFromJsonAsync<NotificationListResponse>("/api/v1/notifications/");
        after!.Items.Should().NotContain(n => n.Type == NotificationType.TeamJoinRequest);
        var rejoin = await (await _alice.PostAsync($"/api/v1/teams/join/{_token}", null)).Content.ReadFromJsonAsync<TeamJoinResultDto>();
        rejoin!.Joined.Should().BeTrue();
    }

    [Fact]
    public async Task Reject_applies_cooldown_and_withdraw_applies_cooldown()
    {
        var a = await (await _alice.PostAsync($"/api/v1/teams/join/{_token}", null)).Content.ReadFromJsonAsync<TeamJoinResultDto>();
        (await _owner.PostAsync($"/api/v1/teams/{_slug}/join-requests/{a!.PendingRequestId}/reject", null)).EnsureSuccessStatusCode();
        var retry = await _alice.PostAsync($"/api/v1/teams/join/{_token}", null);
        retry.StatusCode.Should().Be((HttpStatusCode)429);
        (await retry.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("join_cooldown");

        var b = await (await _bob.PostAsync($"/api/v1/teams/join/{_token}", null)).Content.ReadFromJsonAsync<TeamJoinResultDto>();
        // Only the requester can withdraw.
        (await _alice.PostAsync($"/api/v1/teams/join-requests/{b!.PendingRequestId}/withdraw", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _bob.PostAsync($"/api/v1/teams/join-requests/{b.PendingRequestId}/withdraw", null)).EnsureSuccessStatusCode();
        (await _bob.PostAsync($"/api/v1/teams/join/{_token}", null)).StatusCode.Should().Be((HttpStatusCode)429);
        (await _owner.GetFromJsonAsync<PendingJoinCountDto>("/api/v1/notifications/pending-join-count"))!.Count.Should().Be(0);
    }

    [Fact]
    public async Task Open_join_conversion_requires_confirmation_and_approves_pending_atomically()
    {
        var a = await (await _alice.PostAsync($"/api/v1/teams/join/{_token}", null)).Content.ReadFromJsonAsync<TeamJoinResultDto>();
        a!.Joined.Should().BeFalse();

        // Non-owner cannot change join settings; owner needs explicit confirmation.
        (await _alice.PutAsJsonAsync($"/api/v1/teams/{_slug}/settings", new UpdateTeamSettingsRequest(null, null, TeamJoinMode.OpenJoin, null)))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        var noConfirm = await _owner.PutAsJsonAsync($"/api/v1/teams/{_slug}/settings", new UpdateTeamSettingsRequest(null, null, TeamJoinMode.OpenJoin, null));
        noConfirm.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await _owner.PutAsJsonAsync($"/api/v1/teams/{_slug}/settings", new UpdateTeamSettingsRequest(null, null, null, MemberRole.Admin)))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var ok = await _owner.PutAsJsonAsync(
            $"/api/v1/teams/{_slug}/settings",
            new UpdateTeamSettingsRequest(null, null, TeamJoinMode.OpenJoin, MemberRole.Member, ConfirmOpenJoinApprovePending: true));
        ok.EnsureSuccessStatusCode();
        var result = await ok.Content.ReadFromJsonAsync<UpdateTeamSettingsResult>();
        result!.ApprovedCount.Should().Be(1);
        result.Team.JoinMode.Should().Be(TeamJoinMode.OpenJoin);
        result.Team.DefaultJoinRole.Should().Be(MemberRole.Member);

        var members = await _owner.GetFromJsonAsync<List<TeamMemberDto>>($"/api/v1/teams/{_slug}/members");
        members!.Single(m => m.UserId == _aliceId).Role.Should().Be(MemberRole.Member);
        (await _owner.GetFromJsonAsync<PendingJoinCountDto>("/api/v1/notifications/pending-join-count"))!.Count.Should().Be(0);

        // Open join: Bob joins immediately with the default role.
        var bob = await (await _bob.PostAsync($"/api/v1/teams/join/{_token}", null)).Content.ReadFromJsonAsync<TeamJoinResultDto>();
        bob!.Joined.Should().BeTrue();
        bob.Role.Should().Be(MemberRole.Member);

        // And back to request-to-join needs no confirmation.
        (await _owner.PutAsJsonAsync($"/api/v1/teams/{_slug}/settings", new UpdateTeamSettingsRequest(null, null, TeamJoinMode.RequestToJoin, null)))
            .EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Rotating_the_link_invalidates_the_old_token_immediately()
    {
        var rotated = await (await _owner.PostAsJsonAsync($"/api/v1/teams/{_slug}/join-link/rotate", new RotateTeamJoinLinkRequest(null)))
            .Content.ReadFromJsonAsync<TeamJoinLinkDto>();
        rotated!.Token.Should().NotBe(_token);

        (await _alice.PostAsync($"/api/v1/teams/join/{_token}", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _alice.PostAsync($"/api/v1/teams/join/{rotated.Token}", null)).EnsureSuccessStatusCode();
        (await _owner.GetFromJsonAsync<TeamJoinLinkDto>($"/api/v1/teams/{_slug}/join-link"))!.Token.Should().Be(rotated.Token);
    }

    [Fact]
    public async Task Leave_removes_membership_and_grants_but_owner_must_transfer()
    {
        (await _owner.PostAsync($"/api/v1/teams/{_slug}/leave", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);

        await JoinAndApproveAsync(_alice, MemberRole.Member);
        (await _owner.PostAsJsonAsync(
                $"/api/v1/teams/{_slug}/channels",
                new CreateChannelRequest("secret", null, ChannelVisibility.Restricted, ChannelKind.File)))
            .EnsureSuccessStatusCode();
        (await _owner.PutAsJsonAsync($"/api/v1/teams/{_slug}/channels/secret/members", new UpsertChannelMemberRequest(_aliceId, false)))
            .EnsureSuccessStatusCode();
        (await _alice.GetAsync($"/api/v1/teams/{_slug}/channels/secret")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await _alice.PostAsync($"/api/v1/teams/{_slug}/leave", null)).EnsureSuccessStatusCode();
        (await _alice.GetAsync($"/api/v1/teams/{_slug}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.ChannelMembers.CountAsync(m => m.UserId == _aliceId)).Should().Be(0);
    }

    [Fact]
    public async Task Admin_cannot_remove_admin_and_owner_can()
    {
        await JoinAndApproveAsync(_alice, MemberRole.Member);
        await JoinAndApproveAsync(_bob, MemberRole.Member);
        (await _owner.PutAsJsonAsync($"/api/v1/teams/{_slug}/members/{_aliceId}/role", new ChangeMemberRoleRequest(MemberRole.Admin)))
            .EnsureSuccessStatusCode();
        (await _owner.PutAsJsonAsync($"/api/v1/teams/{_slug}/members/{_bobId}/role", new ChangeMemberRoleRequest(MemberRole.Admin)))
            .EnsureSuccessStatusCode();

        (await _alice.DeleteAsync($"/api/v1/teams/{_slug}/members/{_bobId}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _owner.DeleteAsync($"/api/v1/teams/{_slug}/members/{_bobId}")).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Restricting_a_channel_requires_name_confirmation_and_reports_losers()
    {
        await JoinAndApproveAsync(_alice, MemberRole.Member);
        await JoinAndApproveAsync(_bob, MemberRole.Member);
        (await _owner.PostAsJsonAsync(
                $"/api/v1/teams/{_slug}/channels",
                new CreateChannelRequest("plans", null, ChannelVisibility.Everyone, ChannelKind.File)))
            .EnsureSuccessStatusCode();
        // Bob gets a (read-only) grant that must survive restriction; Alice has none.
        (await _owner.PutAsJsonAsync(
                $"/api/v1/teams/{_slug}/channels/plans/members/bulk",
                new BulkUpsertChannelMembersRequest([new UpsertChannelMemberRequest(_bobId, true)])))
            .EnsureSuccessStatusCode();

        (await _alice.PutAsJsonAsync($"/api/v1/teams/{_slug}/channels/plans/visibility", new SetChannelVisibilityRequest(ChannelVisibility.Restricted)))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        var resp = await _owner.PutAsJsonAsync(
            $"/api/v1/teams/{_slug}/channels/plans/visibility",
            new SetChannelVisibilityRequest(ChannelVisibility.Restricted));
        resp.EnsureSuccessStatusCode();
        var result = await resp.Content.ReadFromJsonAsync<SetChannelVisibilityResult>();
        result!.MembersLosingAccess.Should().Be(1);
        result.Channel.Visibility.Should().Be(ChannelVisibility.Restricted);

        (await _alice.GetAsync($"/api/v1/teams/{_slug}/channels/plans")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _bob.GetAsync($"/api/v1/teams/{_slug}/channels/plans")).StatusCode.Should().Be(HttpStatusCode.OK);

        // Back to Everyone keeps grants dormant.
        (await _owner.PutAsJsonAsync($"/api/v1/teams/{_slug}/channels/plans/visibility", new SetChannelVisibilityRequest(ChannelVisibility.Everyone)))
            .EnsureSuccessStatusCode();
        (await _alice.GetAsync($"/api/v1/teams/{_slug}/channels/plans")).StatusCode.Should().Be(HttpStatusCode.OK);
        var grants = await _owner.GetFromJsonAsync<List<ChannelMemberDto>>($"/api/v1/teams/{_slug}/channels/plans/members");
        grants!.Should().ContainSingle(g => g.DisplayName == "Bob" && g.ReadOnly);
    }

    [Fact]
    public async Task Accessible_search_applies_live_visibility_and_grants()
    {
        await JoinAndApproveAsync(_alice, MemberRole.Member);
        await JoinAndApproveAsync(_bob, MemberRole.Member);
        var open = await CreateChannelAsync("openc", ChannelVisibility.Everyone);
        var secret = await CreateChannelAsync("secretc", ChannelVisibility.Restricted);
        await SeedFileAsync(open, "report-open.txt");
        await SeedFileAsync(secret, "report-secret.txt");
        (await _owner.PutAsJsonAsync($"/api/v1/teams/{_slug}/channels/secretc/members", new UpsertChannelMemberRequest(_bobId, true)))
            .EnsureSuccessStatusCode();

        async Task<string[]> Names(HttpClient c) =>
            (await c.GetFromJsonAsync<SearchResponse>("/api/v1/search?q=report&scope=accessible"))!.Hits.Select(h => h.Name).Order().ToArray();

        (await Names(_alice)).Should().Equal("report-open.txt");
        (await Names(_bob)).Should().Equal("report-open.txt", "report-secret.txt");
        (await Names(_owner)).Should().Equal("report-open.txt", "report-secret.txt");

        // Revoking the grant takes effect on the very next query (no ACL cache).
        (await _owner.DeleteAsync($"/api/v1/teams/{_slug}/channels/secretc/members/{_bobId}")).EnsureSuccessStatusCode();
        (await Names(_bob)).Should().Equal("report-open.txt");
    }

    // ------------------------------------------------ privacy plan: additional cases

    [Fact]
    public async Task Removed_user_who_rejoins_via_open_join_has_no_channel_grants()
    {
        await JoinAndApproveAsync(_alice, MemberRole.Member);
        await CreateChannelAsync("vault", ChannelVisibility.Restricted);
        await CreateChannelAsync("dormant", ChannelVisibility.Everyone);
        foreach (var channel in new[] { "vault", "dormant" })
        {
            // A grant on an Everyone channel stays dormant until the channel is restricted.
            (await _owner.PutAsJsonAsync($"/api/v1/teams/{_slug}/channels/{channel}/members", new UpsertChannelMemberRequest(_aliceId, false)))
                .EnsureSuccessStatusCode();
        }

        (await _alice.GetAsync($"/api/v1/teams/{_slug}/channels/vault")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await _owner.PutAsJsonAsync(
                $"/api/v1/teams/{_slug}/settings",
                new UpdateTeamSettingsRequest(null, null, TeamJoinMode.OpenJoin, MemberRole.Member, ConfirmOpenJoinApprovePending: true)))
            .EnsureSuccessStatusCode();
        (await _owner.DeleteAsync($"/api/v1/teams/{_slug}/members/{_aliceId}")).EnsureSuccessStatusCode();
        (await _alice.GetAsync($"/api/v1/teams/{_slug}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var rejoin = await (await _alice.PostAsync($"/api/v1/teams/join/{_token}", null)).Content.ReadFromJsonAsync<TeamJoinResultDto>();
        rejoin!.Joined.Should().BeTrue();
        rejoin.Role.Should().Be(MemberRole.Member);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.ChannelMembers.CountAsync(m => m.UserId == _aliceId)).Should().Be(0, "removal wipes every grant, including dormant ones");
        }

        (await _alice.GetAsync($"/api/v1/teams/{_slug}/channels/vault")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _alice.GetAsync($"/api/v1/teams/{_slug}/channels/dormant")).StatusCode.Should().Be(HttpStatusCode.OK);

        // The stale dormant grant must not resurface when the channel is restricted later.
        var restrict = await _owner.PutAsJsonAsync(
            $"/api/v1/teams/{_slug}/channels/dormant/visibility",
            new SetChannelVisibilityRequest(ChannelVisibility.Restricted));
        restrict.EnsureSuccessStatusCode();
        (await restrict.Content.ReadFromJsonAsync<SetChannelVisibilityResult>())!.MembersLosingAccess.Should().Be(1);
        (await _alice.GetAsync($"/api/v1/teams/{_slug}/channels/dormant")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Join_link_fails_cleanly_without_the_data_protection_key_and_rotation_recovers()
    {
        // A restore onto a host without the original key ring: ciphertext exists but cannot be decrypted.
        var foreignCiphertext = new EphemeralDataProtectionProvider()
            .CreateProtector(TeamJoinLinkProtector.Purpose)
            .Protect(_token);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.Database.ExecuteSqlInterpolatedAsync(
                $"""UPDATE teams SET "JoinTokenCiphertext" = {foreignCiphertext} WHERE "Slug" = {_slug}""")).Should().Be(1);
        }

        // Get ("copy link") and Ensure both fail with a clean 409 + code; never a 500 and never the token.
        foreach (var call in new Func<Task<HttpResponseMessage>>[]
                 {
                     () => _owner.GetAsync($"/api/v1/teams/{_slug}/join-link"),
                     () => _owner.PostAsync($"/api/v1/teams/{_slug}/join-link", null),
                 })
        {
            var resp = await call();
            resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
            var raw = await resp.Content.ReadAsStringAsync();
            raw.Should().NotContain(_token);
            JsonDocument.Parse(raw).RootElement.GetProperty("code").GetString().Should().Be("join_link_unavailable");
        }

        // Hash-based lookups are independent of the key ring: the already shared link still resolves.
        (await _alice.GetAsync($"/api/v1/teams/join/{_token}")).StatusCode.Should().Be(HttpStatusCode.OK);

        // Rotation writes fresh ciphertext with the current key ring: everything works again.
        var rotated = await (await _owner.PostAsJsonAsync($"/api/v1/teams/{_slug}/join-link/rotate", new RotateTeamJoinLinkRequest(null)))
            .Content.ReadFromJsonAsync<TeamJoinLinkDto>();
        rotated!.Token.Should().NotBe(_token);
        (await _owner.GetFromJsonAsync<TeamJoinLinkDto>($"/api/v1/teams/{_slug}/join-link"))!.Token.Should().Be(rotated.Token);
        (await _alice.PostAsync($"/api/v1/teams/join/{rotated.Token}", null)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Join_cooldown_after_reject_and_after_withdraw_survives_link_rotation()
    {
        var a = await (await _alice.PostAsync($"/api/v1/teams/join/{_token}", null)).Content.ReadFromJsonAsync<TeamJoinResultDto>();
        (await _owner.PostAsync($"/api/v1/teams/{_slug}/join-requests/{a!.PendingRequestId}/reject", null)).EnsureSuccessStatusCode();
        var b = await (await _bob.PostAsync($"/api/v1/teams/join/{_token}", null)).Content.ReadFromJsonAsync<TeamJoinResultDto>();
        (await _bob.PostAsync($"/api/v1/teams/join-requests/{b!.PendingRequestId}/withdraw", null)).EnsureSuccessStatusCode();

        var rotated = await (await _owner.PostAsJsonAsync($"/api/v1/teams/{_slug}/join-link/rotate", new RotateTeamJoinLinkRequest(null)))
            .Content.ReadFromJsonAsync<TeamJoinLinkDto>();

        foreach (var client in new[] { _alice, _bob })
        {
            var retry = await client.PostAsync($"/api/v1/teams/join/{rotated!.Token}", null);
            retry.StatusCode.Should().Be((HttpStatusCode)429);
            (await retry.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("join_cooldown");
        }

        // The cooldown is time-boxed: once elapsed, requesting again is allowed.
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.ExecuteSqlRawAsync("""UPDATE team_join_requests SET "CooldownUntil" = now() - interval '1 minute'""");
        }

        foreach (var client in new[] { _alice, _bob })
        {
            var again = await (await client.PostAsync($"/api/v1/teams/join/{rotated!.Token}", null)).Content.ReadFromJsonAsync<TeamJoinResultDto>();
            again!.PendingRequestId.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task Leaving_admin_keeps_pending_requests_for_remaining_admins_and_loses_their_notifications()
    {
        await JoinAndApproveAsync(_alice, MemberRole.Member);
        (await _owner.PutAsJsonAsync($"/api/v1/teams/{_slug}/members/{_aliceId}/role", new ChangeMemberRoleRequest(MemberRole.Admin)))
            .EnsureSuccessStatusCode();

        var pending = await (await _bob.PostAsync($"/api/v1/teams/join/{_token}", null)).Content.ReadFromJsonAsync<TeamJoinResultDto>();
        pending!.PendingRequestId.Should().NotBeNull();

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.Notifications.CountAsync(n => n.UserId == _aliceId && n.Type == NotificationType.TeamJoinRequest)).Should().Be(1);
        }

        (await _alice.PostAsync($"/api/v1/teams/{_slug}/leave", null)).EnsureSuccessStatusCode();

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.Notifications.CountAsync(n => n.UserId == _aliceId)).Should().Be(0, "the leaver's team notifications are removed");
            var request = await db.TeamJoinRequests.AsNoTracking().SingleAsync(r => r.Id == pending.PendingRequestId);
            request.Status.Should().Be(TeamJoinRequestStatus.Pending, "leaving does not decide other people's requests");
            (await db.Notifications.CountAsync(n => n.Type == NotificationType.TeamJoinRequest && n.UserId != _aliceId))
                .Should().Be(1, "the remaining Admin (owner) still has the notification");
        }

        var visibleToOwner = await _owner.GetFromJsonAsync<List<TeamJoinRequestDto>>($"/api/v1/teams/{_slug}/join-requests");
        visibleToOwner!.Should().ContainSingle(r => r.UserId == _bobId);
        (await _owner.GetFromJsonAsync<PendingJoinCountDto>("/api/v1/notifications/pending-join-count"))!.Count.Should().Be(1);
        (await _alice.GetFromJsonAsync<PendingJoinCountDto>("/api/v1/notifications/pending-join-count"))!.Count.Should().Be(0);
        (await _alice.GetAsync($"/api/v1/teams/{_slug}/join-requests")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Open_join_conversion_over_the_pending_cap_fails_with_documented_error_and_applies_nothing()
    {
        const int cap = TeamJoinOptions.DefaultMaxOpenJoinPendingApprove;
        var teamId = (await _owner.GetFromJsonAsync<TeamDto>($"/api/v1/teams/{_slug}"))!.Id;

        // 501 pending requests from users that do not exist: cheap to seed, and never reaches the approval loop.
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.TeamJoinRequests.AddRange(Enumerable.Range(0, cap + 1).Select(i => new TeamJoinRequestEntity
            {
                Id = UuidV7.NewGuid(),
                TeamId = teamId,
                UserId = UuidV7.NewGuid(),
                Status = TeamJoinRequestStatus.Pending,
                CreatedAt = DateTimeOffset.UtcNow.AddSeconds(i),
            }));
            await db.SaveChangesAsync();
        }

        var failed = await _owner.PutAsJsonAsync(
            $"/api/v1/teams/{_slug}/settings",
            new UpdateTeamSettingsRequest("Renamed", null, TeamJoinMode.OpenJoin, MemberRole.Member, ConfirmOpenJoinApprovePending: true));
        failed.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await failed.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be("open_join_too_many_pending");
        body.GetProperty("detail").GetString().Should().Contain(cap.ToString());

        async Task AssertUntouchedAsync(int expectedPending)
        {
            var team = await _owner.GetFromJsonAsync<TeamDto>($"/api/v1/teams/{_slug}");
            team!.JoinMode.Should().Be(TeamJoinMode.RequestToJoin);
            team.DefaultJoinRole.Should().Be(MemberRole.Viewer);
            team.Name.Should().Be("JoinTeam", "name/default-role edits roll back with the failed conversion");

            await using var scope = _factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.TeamJoinRequests.CountAsync(r => r.TeamId == teamId && r.Status == TeamJoinRequestStatus.Pending)).Should().Be(expectedPending);
            (await db.TeamMembers.CountAsync(m => m.TeamId == teamId)).Should().Be(1);
            (await db.AuditEvents.CountAsync(a => a.TeamId == teamId && a.EventType == AuditEventType.TeamJoinRequestApprove)).Should().Be(0);
            (await db.AuditEvents.CountAsync(a => a.TeamId == teamId && a.EventType == AuditEventType.TeamJoinModeChange)).Should().Be(0);
        }

        await AssertUntouchedAsync(cap + 1);

        // Exactly at the cap the conversion goes through (ghost users are closed as rejected, never made members).
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var one = await db.TeamJoinRequests.OrderBy(r => r.CreatedAt).FirstAsync(r => r.TeamId == teamId);
            db.TeamJoinRequests.Remove(one);
            await db.SaveChangesAsync();
        }

        var ok = await _owner.PutAsJsonAsync(
            $"/api/v1/teams/{_slug}/settings",
            new UpdateTeamSettingsRequest(null, null, TeamJoinMode.OpenJoin, MemberRole.Member, ConfirmOpenJoinApprovePending: true));
        ok.EnsureSuccessStatusCode();
        (await ok.Content.ReadFromJsonAsync<UpdateTeamSettingsResult>())!.ApprovedCount.Should().Be(0);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.TeamJoinRequests.CountAsync(r => r.TeamId == teamId && r.Status == TeamJoinRequestStatus.Pending)).Should().Be(0);
            (await db.TeamJoinRequests.CountAsync(r => r.TeamId == teamId && r.Status == TeamJoinRequestStatus.Rejected)).Should().Be(cap);
            (await db.TeamMembers.CountAsync(m => m.TeamId == teamId)).Should().Be(1);
        }
    }

    // ---------------------------------------------------------------- helpers

    private async Task JoinAndApproveAsync(HttpClient client, MemberRole role)
    {
        var joined = await (await client.PostAsync($"/api/v1/teams/join/{_token}", null)).Content.ReadFromJsonAsync<TeamJoinResultDto>();
        (await _owner.PostAsJsonAsync($"/api/v1/teams/{_slug}/join-requests/{joined!.PendingRequestId}/approve", new ApproveTeamJoinRequestRequest(role)))
            .EnsureSuccessStatusCode();
    }

    private async Task<Guid> CreateChannelAsync(string name, ChannelVisibility visibility)
    {
        var resp = await _owner.PostAsJsonAsync(
            $"/api/v1/teams/{_slug}/channels",
            new CreateChannelRequest(name, null, visibility, ChannelKind.File));
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<ChannelDto>())!.Id;
    }

    private async Task SeedFileAsync(Guid channelId, string name)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTimeOffset.UtcNow;
        db.Files.Add(new FileEntity
        {
            Id = UuidV7.NewGuid(),
            ChannelId = channelId,
            Name = name,
            ContentType = "text/plain",
            SizeBytes = 1,
            Sha256 = new string('a', 64),
            StorageKey = "seed/" + Guid.NewGuid(),
            UploadedBy = _aliceId,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
    }

    private static async Task<Guid> CreateUserAsync(UserManager<AppUser> users, string email, string name)
    {
        var user = new AppUser
        {
            Id = UuidV7.NewGuid(),
            Email = email,
            UserName = email,
            DisplayName = name,
            UsernameSlug = email.Split('@')[0].Replace(".", ""),
            EmailConfirmed = true,
            Status = "active",
        };
        (await users.CreateAsync(user, "UserPassword1!")).Succeeded.Should().BeTrue();
        return user.Id;
    }

    private async Task<HttpClient> LoginAsync(string email, string password)
    {
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });
        (await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password))).EnsureSuccessStatusCode();
        var csrf = await client.GetAsync("/api/v1/auth/csrf");
        csrf.EnsureSuccessStatusCode();
        var body = await csrf.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", body.GetProperty("token").GetString());
        return client;
    }
}
