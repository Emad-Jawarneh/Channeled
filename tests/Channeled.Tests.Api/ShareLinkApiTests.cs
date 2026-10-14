using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Channeled.Application.Files;
using Channeled.Application.Identity;
using Channeled.Application.PublicLinks;
using Channeled.Application.Teams;
using Channeled.Domain.Audit;
using Channeled.Domain.Teams;
using Channeled.Infrastructure.Identity;
using Channeled.Infrastructure.Persistence;
using Channeled.Infrastructure.PublicLinks;
using Channeled.Infrastructure.Teams;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Channeled.Tests.Api;

[Collection("Api")]
public class ShareLinkApiTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private const string Payload = "public-share-payload-0123456789";

    private readonly AuthApiFactory _factory;
    private HttpClient _owner = null!;
    private HttpClient _member = null!;
    private HttpClient _outsider = null!;
    private HttpClient _anon = null!;
    private Guid _teamId;
    private Guid _memberId;
    private string _slug = "";

    public ShareLinkApiTests(AuthApiFactory factory) => _factory = factory;

    private sealed record Shared(HttpClient Owner, HttpClient Member, HttpClient Outsider, HttpClient Anon, Guid MemberId);

    // The login endpoint sits behind a global 20/min limiter, so users/logins are set up once per factory
    // (i.e. once per class); every test then works inside its own freshly created team.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<AuthApiFactory, Task<Shared>> SharedByFactory = new();
    private static readonly object Gate = new();

    public async Task InitializeAsync()
    {
        Task<Shared>? setup;
        lock (Gate)
        {
            if (!SharedByFactory.TryGetValue(_factory, out setup))
            {
                setup = SetupSharedAsync(_factory);
                SharedByFactory.Add(_factory, setup);
            }
        }

        var shared = await setup;
        _owner = shared.Owner;
        _member = shared.Member;
        _outsider = shared.Outsider;
        _anon = shared.Anon;
        _memberId = shared.MemberId;

        var team = await (await _owner.PostAsJsonAsync(
                "/api/v1/teams/", new CreateTeamRequest("Shares " + Guid.NewGuid().ToString("N")[..8], null)))
            .Content.ReadFromJsonAsync<TeamDto>();
        _slug = team!.Slug;
        _teamId = team.Id;
        (await _owner.PostAsJsonAsync(
            $"/api/v1/teams/{_slug}/channels",
            new CreateChannelRequest("general", null, ChannelVisibility.Everyone, ChannelKind.File))).EnsureSuccessStatusCode();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.TeamMembers.Add(new TeamMemberEntity
        {
            TeamId = _teamId,
            UserId = _memberId,
            Role = MemberRole.Member,
            JoinedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private static async Task<Shared> SetupSharedAsync(AuthApiFactory factory)
    {
        await factory.ResetDatabaseAsync();

        await using var scope = factory.Services.CreateAsyncScope();
        var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
        await auth.SeedOperatorAsync(new SeedOperatorRequest("owner@example.com", "Owner", "OwnerPassword1!"));

        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var member = await CreateUserAsync(users, "member@example.com", "MemberPass1!!");
        await CreateUserAsync(users, "outsider@example.com", "OutsiderPass1!");

        var owner = CreateClient(factory);
        var memberClient = CreateClient(factory);
        var outsider = CreateClient(factory);
        var anon = CreateClient(factory, handleCookies: false);
        await LoginAsync(owner, "owner@example.com", "OwnerPassword1!");
        await LoginAsync(memberClient, "member@example.com", "MemberPass1!!");
        await LoginAsync(outsider, "outsider@example.com", "OutsiderPass1!");
        return new Shared(owner, memberClient, outsider, anon, member.Id);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ------------------------------------------------------------------ create / list / metadata

    [Fact]
    public async Task Create_returns_token_once_and_list_exposes_metadata_only()
    {
        var fileId = await UploadAsync(_owner, "general", "a.txt", Payload);
        var created = await CreateLinkAsync(_owner, fileId, maxDownloads: 2);
        created.Token.Should().HaveLength(64);
        created.Path.Should().Be("/s/" + created.Token);

        var listResp = await _owner.GetAsync($"/api/v1/teams/{_slug}/channels/general/share-links");
        listResp.EnsureSuccessStatusCode();
        var raw = await listResp.Content.ReadAsStringAsync();
        raw.Should().NotContain(created.Token);
        raw.ToLowerInvariant().Should().NotContain("tokenhash").And.NotContain("token");
        var list = JsonSerializer.Deserialize<List<ShareLinkDto>>(raw, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        list.Should().ContainSingle(l => l.Id == created.Id && l.Status == "active" && l.FileName == "a.txt");

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = await db.FileShareLinks.AsNoTracking().SingleAsync(l => l.TeamId == _teamId);
        row.TokenHash.Should().NotBe(created.Token);
        row.TokenHash.Should().HaveLength(64);

        var types = await db.AuditEvents.AsNoTracking().Select(a => a.EventType).ToListAsync();
        types.Should().Contain(AuditEventType.FileShareCreate);
    }

    [Fact]
    public async Task Outsider_cannot_create_and_member_list_only_sees_own()
    {
        var fileId = await UploadAsync(_owner, "general", "a.txt", Payload);
        (await _outsider.PostAsJsonAsync(
            $"/api/v1/teams/{_slug}/channels/general/items/{fileId}/share-links",
            new CreateShareLinkRequest(null, null))).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var ownerLink = await CreateLinkAsync(_owner, fileId);
        var memberLink = await CreateLinkAsync(_member, fileId);

        var memberList = await _member.GetFromJsonAsync<List<ShareLinkDto>>($"/api/v1/teams/{_slug}/channels/general/share-links");
        memberList!.Select(l => l.Id).Should().BeEquivalentTo([memberLink.Id]);
        var ownerList = await _owner.GetFromJsonAsync<List<ShareLinkDto>>($"/api/v1/teams/{_slug}/channels/general/share-links");
        ownerList!.Select(l => l.Id).Should().BeEquivalentTo([ownerLink.Id, memberLink.Id]);
    }

    [Fact]
    public async Task Creator_or_admin_can_revoke_other_member_cannot()
    {
        var fileId = await UploadAsync(_owner, "general", "a.txt", Payload);
        var ownerLink = await CreateLinkAsync(_owner, fileId);
        var memberLink = await CreateLinkAsync(_member, fileId);

        // plain member cannot revoke someone else's link (404, no existence leak)
        (await _member.DeleteAsync($"/api/v1/teams/{_slug}/share-links/{ownerLink.Id}")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
        (await _outsider.DeleteAsync($"/api/v1/teams/{_slug}/share-links/{ownerLink.Id}")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);

        // creator revokes own
        (await _member.DeleteAsync($"/api/v1/teams/{_slug}/share-links/{memberLink.Id}")).EnsureSuccessStatusCode();
        // owner (Admin+) revokes any (here: their own, then idempotent repeat)
        (await _owner.DeleteAsync($"/api/v1/teams/{_slug}/share-links/{ownerLink.Id}")).EnsureSuccessStatusCode();
        (await _owner.DeleteAsync($"/api/v1/teams/{_slug}/share-links/{ownerLink.Id}")).EnsureSuccessStatusCode();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.FileShareLinks.AsNoTracking().CountAsync(l => l.TeamId == _teamId && l.RevokedAt != null)).Should().Be(2);
        (await db.AuditEvents.AsNoTracking()
            .CountAsync(a => a.TeamId == _teamId && a.EventType == AuditEventType.FileShareRevoke)).Should().Be(2);

        // admin revokes a member-created link
        var again = await CreateLinkAsync(_member, fileId);
        (await _owner.DeleteAsync($"/api/v1/teams/{_slug}/share-links/{again.Id}")).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Platform_admin_can_list_and_revoke_all_others_get_404()
    {
        await ExecAsync("TRUNCATE TABLE file_share_links");
        var fileId = await UploadAsync(_owner, "general", "a.txt", Payload);
        await CreateLinkAsync(_owner, fileId);
        await CreateLinkAsync(_member, fileId);

        (await _member.GetAsync("/api/v1/admin/share-links")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _member.PostAsync("/api/v1/admin/share-links/revoke-all", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var list = await _owner.GetFromJsonAsync<List<ShareLinkDto>>("/api/v1/admin/share-links?activeOnly=true");
        list.Should().HaveCount(2);

        var revoke = await _owner.PostAsync("/api/v1/admin/share-links/revoke-all", null);
        revoke.EnsureSuccessStatusCode();
        (await revoke.Content.ReadFromJsonAsync<AdminRevokeAllShareLinksResult>())!.Revoked.Should().Be(2);
        (await _owner.GetFromJsonAsync<List<ShareLinkDto>>("/api/v1/admin/share-links?activeOnly=true"))
            .Should().BeEmpty();
    }

    // ------------------------------------------------------------------ anonymous happy path

    [Fact]
    public async Task Anonymous_info_claim_download_round_trip_with_range_and_ticket_byte_cap()
    {
        var fileId = await UploadAsync(_owner, "general", "report.txt", Payload);
        var link = await CreateLinkAsync(_owner, fileId, maxDownloads: 3);

        (await _anon.GetAsync("/api/v1/public/shares/" + new string('a', 64))).StatusCode
            .Should().Be(HttpStatusCode.NotFound);

        var info = await _anon.GetFromJsonAsync<PublicShareInfoDto>($"/api/v1/public/shares/{link.Token}");
        info!.FileName.Should().Be("report.txt");
        info.SizeBytes.Should().Be(Payload.Length);
        info.RemainingDownloads.Should().Be(3);

        var claim = await ClaimAsync(link.Token);
        claim.StatusCode.Should().Be(HttpStatusCode.OK);
        var ticket = (await claim.Content.ReadFromJsonAsync<ShareClaimDto>())!;

        var download = await _anon.GetAsync(ticket.DownloadPath);
        download.StatusCode.Should().Be(HttpStatusCode.OK);
        download.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        (await download.Content.ReadAsStringAsync()).Should().Be(Payload);

        using var rangeReq = new HttpRequestMessage(HttpMethod.Get, ticket.DownloadPath);
        rangeReq.Headers.Range = new RangeHeaderValue(0, 4);
        var ranged = await _anon.SendAsync(rangeReq);
        ranged.StatusCode.Should().Be(HttpStatusCode.PartialContent);
        (await ranged.Content.ReadAsStringAsync()).Should().Be(Payload[..5]);

        // wrong ticket / tampered ticket
        (await _anon.GetAsync($"/api/v1/public/shares/{link.Token}/download?ticket=garbage")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
        (await _anon.GetAsync($"/api/v1/public/shares/{link.Token}/download")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);

        // byte cap = 2 x size per ticket: S + 5 already served, another full download (S) would exceed 2S
        (await _anon.GetAsync(ticket.DownloadPath)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Authenticated download is unaffected by anon accounting
        (await _owner.GetAsync($"/api/v1/teams/{_slug}/channels/general/items/{fileId}/content")).EnsureSuccessStatusCode();
    }

    // ------------------------------------------------------------------ concurrency + idempotency

    [Fact]
    public async Task Concurrent_claims_with_max_downloads_one_yield_exactly_one_ticket()
    {
        var fileId = await UploadAsync(_owner, "general", "a.txt", Payload);
        var link = await CreateLinkAsync(_owner, fileId, maxDownloads: 1);

        var responses = await Task.WhenAll(Enumerable.Range(0, 24).Select(_ => ClaimAsync(link.Token)));
        responses.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.NotFound).Should().Be(23);

        (await GetLinkAsync(link.Id)).DownloadCount.Should().Be(1);
    }

    [Fact]
    public async Task Claim_is_idempotent_per_token_and_key_within_window()
    {
        var fileId = await UploadAsync(_owner, "general", "a.txt", Payload);
        var link = await CreateLinkAsync(_owner, fileId, maxDownloads: 5);

        var parallel = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => ClaimAsync(link.Token, "same-key")));
        parallel.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK);
        var tickets = new List<string>();
        foreach (var r in parallel)
        {
            tickets.Add((await r.Content.ReadFromJsonAsync<ShareClaimDto>())!.Ticket);
        }

        tickets.Distinct().Should().HaveCount(1);
        (await GetLinkAsync(link.Id)).DownloadCount.Should().Be(1);

        // sequential retry with same key: still the same ticket, still one consume
        var retry = await (await ClaimAsync(link.Token, "same-key")).Content.ReadFromJsonAsync<ShareClaimDto>();
        retry!.Ticket.Should().Be(tickets[0]);
        (await GetLinkAsync(link.Id)).DownloadCount.Should().Be(1);

        // a different key consumes another download (no refund, no free pass)
        (await ClaimAsync(link.Token, "other-key")).EnsureSuccessStatusCode();
        (await GetLinkAsync(link.Id)).DownloadCount.Should().Be(2);

        // idempotency is scoped to the token hash: same key on a different link is a separate claim
        var link2 = await CreateLinkAsync(_owner, fileId, maxDownloads: 5);
        (await ClaimAsync(link2.Token, "same-key")).EnsureSuccessStatusCode();
        (await GetLinkAsync(link2.Id)).DownloadCount.Should().Be(1);
    }

    // ------------------------------------------------------------------ expiry semantics

    [Fact]
    public async Task Claimed_ticket_outlives_link_expiry_but_new_claims_are_blocked()
    {
        var fileId = await UploadAsync(_owner, "general", "a.txt", Payload);
        var link = await CreateLinkAsync(_owner, fileId, maxDownloads: 5);
        var ticket = await (await ClaimAsync(link.Token)).Content.ReadFromJsonAsync<ShareClaimDto>();

        await ExecAsync($"UPDATE file_share_links SET expires_at = now() - interval '1 hour' WHERE team_id = '{_teamId}'");

        (await _anon.GetAsync(ticket!.DownloadPath)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await ClaimAsync(link.Token)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _anon.GetAsync($"/api/v1/public/shares/{link.Token}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Ticket_with_unknown_key_or_expired_is_rejected_and_current_key_accepted()
    {
        var fileId = await UploadAsync(_owner, "general", "a.txt", Payload);
        var link = await CreateLinkAsync(_owner, fileId);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var linkId = (await db.FileShareLinks.AsNoTracking().SingleAsync(l => l.TeamId == _teamId)).Id;

        var now = DateTimeOffset.UtcNow;
        var evil = new HmacTicketSigner(new FixedOptions(new PublicFileLinksOptions { HmacKey = "attacker-key-attacker-key" }))
            .Sign(new ShareTicket(linkId, Guid.NewGuid(), now, now.AddHours(1)));
        (await _anon.GetAsync($"/api/v1/public/shares/{link.Token}/download?ticket={Uri.EscapeDataString(evil)}"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // A ticket signed with the app's configured key validates (dual-key rotation is covered in unit tests).
        var good = new HmacTicketSigner(new FixedOptions(new PublicFileLinksOptions { HmacKey = AuthApiFactory.TestHmacKey }))
            .Sign(new ShareTicket(linkId, Guid.NewGuid(), now, now.AddHours(1)));
        (await _anon.GetAsync($"/api/v1/public/shares/{link.Token}/download?ticket={Uri.EscapeDataString(good)}"))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // expired ticket is rejected
        var expired = new HmacTicketSigner(new FixedOptions(new PublicFileLinksOptions { HmacKey = AuthApiFactory.TestHmacKey }))
            .Sign(new ShareTicket(linkId, Guid.NewGuid(), now.AddHours(-3), now.AddHours(-1)));
        (await _anon.GetAsync($"/api/v1/public/shares/{link.Token}/download?ticket={Uri.EscapeDataString(expired)}"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ------------------------------------------------------------------ revocation triggers

    [Fact]
    public async Task Revoke_aborts_downloads_for_already_claimed_ticket()
    {
        var fileId = await UploadAsync(_owner, "general", "a.txt", Payload);
        var link = await CreateLinkAsync(_owner, fileId);
        var ticket = await (await ClaimAsync(link.Token)).Content.ReadFromJsonAsync<ShareClaimDto>();

        (await _owner.DeleteAsync($"/api/v1/teams/{_slug}/share-links/{link.Id}")).EnsureSuccessStatusCode();

        (await _anon.GetAsync(ticket!.DownloadPath)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ClaimAsync(link.Token)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Trashing_file_revokes_links_and_restore_does_not_resurrect()
    {
        var fileId = await UploadAsync(_owner, "general", "a.txt", Payload);
        var link = await CreateLinkAsync(_owner, fileId);
        var ticket = await (await ClaimAsync(link.Token)).Content.ReadFromJsonAsync<ShareClaimDto>();

        (await _owner.DeleteAsync($"/api/v1/teams/{_slug}/channels/general/items/{fileId}")).EnsureSuccessStatusCode();

        var row = await GetLinkAsync(link.Id);
        row.RevokedAt.Should().NotBeNull();
        row.RevokeReason.Should().Be(ShareLinkRevoker.ReasonTrash);
        (await _anon.GetAsync(ticket!.DownloadPath)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await _owner.PostAsync($"/api/v1/teams/{_slug}/channels/general/items/{fileId}/restore", null)).EnsureSuccessStatusCode();
        (await _anon.GetAsync(ticket.DownloadPath)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ClaimAsync(link.Token)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Trashing_parent_folder_revokes_links_of_all_nested_files()
    {
        var parent = await CreateFolderAsync("parent", null);
        var child = await CreateFolderAsync("child", parent);
        var inChild = await UploadAsync(_owner, "general", "deep.txt", Payload, child);
        var inParent = await UploadAsync(_owner, "general", "mid.txt", Payload, parent);
        var outside = await UploadAsync(_owner, "general", "root.txt", Payload);

        var l1 = await CreateLinkAsync(_owner, inChild);
        var l2 = await CreateLinkAsync(_owner, inParent);
        var l3 = await CreateLinkAsync(_owner, outside);

        (await _owner.DeleteAsync($"/api/v1/teams/{_slug}/channels/general/items/{parent}")).EnsureSuccessStatusCode();

        (await GetLinkAsync(l1.Id)).RevokeReason.Should().Be(ShareLinkRevoker.ReasonFolderTrash);
        (await GetLinkAsync(l2.Id)).RevokeReason.Should().Be(ShareLinkRevoker.ReasonFolderTrash);
        (await GetLinkAsync(l3.Id)).RevokedAt.Should().BeNull();
    }

    [Fact]
    public async Task Channel_move_revokes_links_in_same_transaction_and_keeps_original_channel()
    {
        (await _owner.PostAsJsonAsync(
            $"/api/v1/teams/{_slug}/channels",
            new CreateChannelRequest("other", null, ChannelVisibility.Everyone, ChannelKind.File))).EnsureSuccessStatusCode();
        var fileId = await UploadAsync(_owner, "general", "a.txt", Payload);
        var link = await CreateLinkAsync(_owner, fileId);
        var ticket = await (await ClaimAsync(link.Token)).Content.ReadFromJsonAsync<ShareClaimDto>();
        var before = await GetLinkAsync(link.Id);

        var move = await _owner.PostAsJsonAsync(
            $"/api/v1/teams/{_slug}/channels/general/items/{fileId}/move",
            new MoveItemRequest(null, "other"));
        move.EnsureSuccessStatusCode();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var otherId = (await db.Channels.AsNoTracking().SingleAsync(c => c.TeamId == _teamId && c.Name == "other")).Id;
        var file = await db.Files.AsNoTracking().SingleAsync(f => f.Id == fileId);
        file.ChannelId.Should().Be(otherId);

        var row = await db.FileShareLinks.AsNoTracking().SingleAsync(l => l.Id == link.Id);
        row.RevokedAt.Should().NotBeNull();
        row.RevokeReason.Should().Be(ShareLinkRevoker.ReasonMove);
        row.OriginalChannelId.Should().Be(before.OriginalChannelId);
        row.ChannelId.Should().Be(otherId, "ON UPDATE CASCADE carries channel_id; link is revoked in the same txn");
        row.FileId.Should().Be(fileId);

        (await _anon.GetAsync(ticket!.DownloadPath)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ClaimAsync(link.Token)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // folders cannot cross channels
        var folder = await CreateFolderAsync("f", null);
        (await _owner.PostAsJsonAsync(
            $"/api/v1/teams/{_slug}/channels/general/items/{folder}/move",
            new MoveItemRequest(null, "other"))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Archiving_channel_revokes_its_links()
    {
        var fileId = await UploadAsync(_owner, "general", "a.txt", Payload);
        var link = await CreateLinkAsync(_owner, fileId);

        (await _owner.PostAsync($"/api/v1/teams/{_slug}/channels/general/archive", null)).EnsureSuccessStatusCode();

        (await GetLinkAsync(link.Id)).RevokeReason.Should().Be(ShareLinkRevoker.ReasonChannelArchive);
        (await ClaimAsync(link.Token)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Archiving_team_revokes_all_team_links()
    {
        var fileId = await UploadAsync(_owner, "general", "a.txt", Payload);
        var link = await CreateLinkAsync(_owner, fileId);

        (await _owner.PostAsync($"/api/v1/teams/{_slug}/archive", null)).EnsureSuccessStatusCode();

        (await GetLinkAsync(link.Id)).RevokeReason.Should().Be(ShareLinkRevoker.ReasonTeamArchive);
    }

    [Fact]
    public async Task Kill_switch_disables_claims_and_info_without_touching_rows()
    {
        var fileId = await UploadAsync(_owner, "general", "a.txt", Payload);
        var link = await CreateLinkAsync(_owner, fileId);
        var ticket = await (await ClaimAsync(link.Token)).Content.ReadFromJsonAsync<ShareClaimDto>();

        using var off = _factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
            c.AddInMemoryCollection(new Dictionary<string, string?> { ["PublicFileLinks:Enabled"] = "false" })));
        using var offAnon = off.CreateClient();
        (await offAnon.GetAsync($"/api/v1/public/shares/{link.Token}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await offAnon.PostAsync($"/api/v1/public/shares/{link.Token}/claim", null)).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
        (await offAnon.GetAsync(ticket!.DownloadPath)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await GetLinkAsync(link.Id)).RevokedAt.Should().BeNull();
    }

    // ------------------------------------------------------------------ purge on real PG

    [Fact]
    public async Task Admin_purge_with_live_links_succeeds_and_nulls_file_id_via_composite_fk()
    {
        var fileId = await UploadAsync(_owner, "general", "a.txt", Payload);
        var link = await CreateLinkAsync(_owner, fileId);
        var ticket = await (await ClaimAsync(link.Token)).Content.ReadFromJsonAsync<ShareClaimDto>();
        var before = await GetLinkAsync(link.Id);

        (await _owner.PostAsync($"/api/v1/admin/files/{fileId}/purge", null)).EnsureSuccessStatusCode();

        var row = await GetLinkAsync(link.Id);
        row.FileId.Should().BeNull("ON DELETE SET NULL (file_id)");
        row.ChannelId.Should().Be(before.ChannelId, "channel_id is NOT NULL and must survive the SET NULL");
        row.OriginalChannelId.Should().Be(before.OriginalChannelId);
        row.FileName.Should().Be("a.txt");
        row.RevokedAt.Should().NotBeNull();
        row.RevokeReason.Should().Be(ShareLinkRevoker.ReasonPurge);

        (await _anon.GetAsync(ticket!.DownloadPath)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Raw_file_delete_with_live_link_only_nulls_file_id_and_fk_has_expected_shape()
    {
        var fileId = await UploadAsync(_owner, "general", "a.txt", Payload);
        var link = await CreateLinkAsync(_owner, fileId);
        var before = await GetLinkAsync(link.Id);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var shape = await db.Database
            .SqlQueryRaw<string>("""
                SELECT (confdeltype::text || confupdtype::text
                        || CASE WHEN confdelsetcols IS NULL THEN 'x' ELSE 's' END) AS "Value"
                FROM pg_constraint WHERE conname = 'fk_file_share_links_file_channel'
                """)
            .SingleAsync();
        shape.Should().Be("ncs");

        // bypass the application entirely: a hard DELETE must not fail nor null channel_id
        await db.Database.ExecuteSqlInterpolatedAsync($"""DELETE FROM files WHERE "Id" = {fileId}""");

        var row = await db.FileShareLinks.AsNoTracking().SingleAsync(l => l.Id == link.Id);
        row.FileId.Should().BeNull();
        row.ChannelId.Should().Be(before.ChannelId);
        row.RevokedAt.Should().BeNull();
    }

    // ------------------------------------------------------------------ anon saturation vs authenticated

    [Fact]
    public async Task Saturated_anonymous_streams_get_429_while_authenticated_downloads_still_work()
    {
        var fileId = await UploadAsync(_owner, "general", "a.txt", Payload);
        var link = await CreateLinkAsync(_owner, fileId, maxDownloads: 10);
        var ticket = await (await ClaimAsync(link.Token)).Content.ReadFromJsonAsync<ShareClaimDto>();

        var limiter = _factory.Services.GetRequiredService<IAnonDownloadLimiter>();
        var leases = new List<IAnonStreamLease>();
        try
        {
            for (var i = 0; i < 10_000; i++)
            {
                var lease = limiter.TryAcquire($"10.0.{i / 250}.{i % 250}");
                if (lease is null)
                {
                    break;
                }

                leases.Add(lease);
            }

            leases.Should().NotBeEmpty();

            var anon = await _anon.GetAsync(ticket!.DownloadPath);
            anon.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

            var auth = await _owner.GetAsync($"/api/v1/teams/{_slug}/channels/general/items/{fileId}/content");
            auth.StatusCode.Should().Be(HttpStatusCode.OK);
            (await auth.Content.ReadAsStringAsync()).Should().Be(Payload);
        }
        finally
        {
            leases.ForEach(l => l.Dispose());
        }

        (await _anon.GetAsync(ticket.DownloadPath)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ------------------------------------------------------------------ helpers

    private sealed class FixedOptions(PublicFileLinksOptions value) : Microsoft.Extensions.Options.IOptionsMonitor<PublicFileLinksOptions>
    {
        public PublicFileLinksOptions CurrentValue => value;
        public PublicFileLinksOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<PublicFileLinksOptions, string?> listener) => null;
    }

    private async Task<ShareLinkCreatedDto> CreateLinkAsync(HttpClient client, Guid fileId, int? maxDownloads = null)
    {
        var resp = await client.PostAsJsonAsync(
            $"/api/v1/teams/{_slug}/channels/general/items/{fileId}/share-links",
            new CreateShareLinkRequest(null, maxDownloads));
        resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());
        return (await resp.Content.ReadFromJsonAsync<ShareLinkCreatedDto>())!;
    }

    private Task<HttpResponseMessage> ClaimAsync(string token, string? key = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/public/shares/{token}/claim");
        req.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString("N"));
        return _anon.SendAsync(req);
    }

    private async Task<FileShareLinkEntity> GetLinkAsync(Guid id)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.FileShareLinks.AsNoTracking().SingleAsync(l => l.Id == id);
    }

    private async Task ExecAsync(string sql)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.ExecuteSqlRawAsync(sql);
    }

    private async Task<Guid> CreateFolderAsync(string name, Guid? parent)
    {
        var resp = await _owner.PostAsJsonAsync(
            $"/api/v1/teams/{_slug}/channels/general/folders",
            new CreateFolderRequest(name, parent));
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<FileItemDto>())!.Id;
    }

    private async Task<Guid> UploadAsync(HttpClient client, string channel, string name, string text, Guid? folderId = null)
    {
        var payload = Encoding.UTF8.GetBytes(text);
        var sha = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        var start = await (await client.PostAsJsonAsync("/api/v1/uploads/", new StartUploadRequest(
            _slug, channel, name, payload.Length, "text/plain", folderId))).Content.ReadFromJsonAsync<StartUploadResponse>();
        using var part = new ByteArrayContent(payload);
        part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        (await client.PutAsync($"/api/v1/uploads/{start!.UploadId}/parts/1", part)).EnsureSuccessStatusCode();
        var complete = await client.PostAsJsonAsync(
            $"/api/v1/uploads/{start.UploadId}/complete",
            new CompleteUploadRequest(sha, null));
        complete.EnsureSuccessStatusCode();
        return (await complete.Content.ReadFromJsonAsync<FileItemDto>())!.Id;
    }

    private static async Task<AppUser> CreateUserAsync(UserManager<AppUser> users, string email, string password)
    {
        var user = new AppUser
        {
            Id = Channeled.Domain.Identity.UuidV7.NewGuid(),
            Email = email,
            UserName = email,
            DisplayName = email,
                    UsernameSlug = email.Split('@')[0].Replace(".", "")[..30],
            EmailConfirmed = true,
            Status = "active",
        };
        (await users.CreateAsync(user, password)).Succeeded.Should().BeTrue();
        return user;
    }

    private static HttpClient CreateClient(AuthApiFactory factory, bool handleCookies = true) =>
        factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = handleCookies,
        });

    private static async Task LoginAsync(HttpClient client, string email, string password)
    {
        (await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password))).EnsureSuccessStatusCode();
        var csrf = await client.GetAsync("/api/v1/auth/csrf");
        csrf.EnsureSuccessStatusCode();
        var body = await csrf.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", body.GetProperty("token").GetString());
    }
}
