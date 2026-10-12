using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Channeled.Application.Admin;
using Channeled.Application.Files;
using Channeled.Application.Identity;
using Channeled.Application.Personal;
using Channeled.Application.Teams;
using Channeled.Domain.Audit;
using Channeled.Domain.Files;
using Channeled.Domain.Teams;
using Channeled.Infrastructure.Identity;
using Channeled.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Channeled.Tests.Api;

[Collection("Api")]
public class AdminCatalogAndImmutabilityTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private HttpClient _admin = null!;
    private HttpClient _member = null!;

    public AdminCatalogAndImmutabilityTests(AuthApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.ResetDatabaseAsync();
        await using var scope = _factory.Services.CreateAsyncScope();
        var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
        await auth.SeedOperatorAsync(new SeedOperatorRequest("admin@example.com", "Admin", "AdminPassword1!"));

        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var member = new AppUser
        {
            Id = Channeled.Domain.Identity.UuidV7.NewGuid(),
            Email = "member@example.com",
            UserName = "member@example.com",
            DisplayName = "Member",
            EmailConfirmed = true,
            Status = "active",
        };
        (await users.CreateAsync(member, "MemberPassword1!")).Succeeded.Should().BeTrue();

        _admin = CreateClient();
        _member = CreateClient();
        await LoginAsync(_admin, "admin@example.com", "AdminPassword1!");
        await LoginAsync(_member, "member@example.com", "MemberPassword1!");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Every_audit_catalog_event_is_emitted()
    {
        // --- Auth ---
        await _admin.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("admin@example.com", "wrong-password!!"));
        await LoginAsync(_admin, "admin@example.com", "AdminPassword1!");

        using (var extra = CreateClient())
        {
            await LoginAsync(extra, "admin@example.com", "AdminPassword1!");
        }

        var sessions = await _admin.GetFromJsonAsync<List<SessionDto>>("/api/v1/auth/sessions");
        var toRevoke = sessions!.First(s => !s.IsCurrent);
        (await _admin.DeleteAsync($"/api/v1/auth/sessions/{toRevoke.Id}")).EnsureSuccessStatusCode();

        (await _admin.PostAsJsonAsync(
            "/api/v1/auth/change-password",
            new ChangePasswordRequest("AdminPassword1!", "AdminPassword2!"))).EnsureSuccessStatusCode();
        await LoginAsync(_admin, "admin@example.com", "AdminPassword2!");

        var forgot = await _admin.PostAsJsonAsync("/api/v1/auth/forgot-password", new ForgotPasswordRequest("admin@example.com"));
        forgot.EnsureSuccessStatusCode();
        // Testing env returns token in AuthAbuse tests via ForgotPasswordForTests — use service:
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
            var tokenResult = await auth.ForgotPasswordForTestsAsync(
                new ForgotPasswordRequest("admin@example.com"),
                new RequestContext("test", "test"));
            tokenResult.Succeeded.Should().BeTrue();
            (await auth.ResetPasswordAsync(
                new ResetPasswordRequest(tokenResult.Value!, "AdminPassword3!"),
                new RequestContext("test", "test"))).Succeeded.Should().BeTrue();
        }

        await LoginAsync(_admin, "admin@example.com", "AdminPassword3!");

        // Platform invite → UserCreate path already from seed; create another via admin users
        var createdUser = await (await _admin.PostAsJsonAsync(
            "/api/v1/admin/users",
            new CreateAdminUserRequest("newbie@example.com", "Newbie", "NewbiePass123!", false, null)))
            .Content.ReadFromJsonAsync<AdminUserDto>();

        // --- Team / channel / invite ---
        var team = await (await _admin.PostAsJsonAsync("/api/v1/teams/", new CreateTeamRequest("AdminTeam", null)))
            .Content.ReadFromJsonAsync<TeamDto>();
        var slug = team!.Slug;

        var invite = await (await _admin.PostAsJsonAsync(
            $"/api/v1/teams/{slug}/invitations",
            new CreateTeamInvitationRequest("member@example.com", MemberRole.Member)))
            .Content.ReadFromJsonAsync<TeamInvitationDto>();

        (await _member.PostAsJsonAsync(
            "/api/v1/teams/invitations/accept",
            new AcceptTeamInvitationRequest(invite!.RawToken!))).EnsureSuccessStatusCode();

        var invite2 = await (await _admin.PostAsJsonAsync(
            $"/api/v1/teams/{slug}/invitations",
            new CreateTeamInvitationRequest("ghost@example.com", MemberRole.Viewer)))
            .Content.ReadFromJsonAsync<TeamInvitationDto>();
        (await _admin.PostAsync($"/api/v1/teams/{slug}/invitations/{invite2!.Id}/revoke", null)).EnsureSuccessStatusCode();

        var memberUser = await FindUserIdAsync("member@example.com");
        (await _admin.PutAsJsonAsync(
            $"/api/v1/teams/{slug}/members/{memberUser}/role",
            new ChangeMemberRoleRequest(MemberRole.Admin))).EnsureSuccessStatusCode();

        (await _admin.PostAsJsonAsync(
            $"/api/v1/teams/{slug}/channels",
            new CreateChannelRequest("general", "g", ChannelVisibility.Open, ChannelKind.File))).EnsureSuccessStatusCode();
        (await _admin.PutAsJsonAsync(
            $"/api/v1/teams/{slug}/channels/general",
            new UpdateChannelRequest(null, "updated"))).EnsureSuccessStatusCode();

        (await _admin.PostAsJsonAsync(
            $"/api/v1/teams/{slug}/channels",
            new CreateChannelRequest("secret", null, ChannelVisibility.Private, ChannelKind.File))).EnsureSuccessStatusCode();
        (await _admin.PutAsJsonAsync(
            $"/api/v1/teams/{slug}/channels/secret/members",
            new UpsertChannelMemberRequest(memberUser, true, false))).EnsureSuccessStatusCode();
        (await _admin.DeleteAsync($"/api/v1/teams/{slug}/channels/secret/members/{memberUser}")).EnsureSuccessStatusCode();
        (await _admin.PostAsync($"/api/v1/teams/{slug}/channels/secret/archive", null)).EnsureSuccessStatusCode();
        (await _admin.DeleteAsync($"/api/v1/teams/{slug}/channels/secret")).EnsureSuccessStatusCode();

        (await _admin.DeleteAsync($"/api/v1/teams/{slug}/members/{memberUser}")).EnsureSuccessStatusCode();

        // --- Files / folders / personal ---
        var fileId = await UploadChannelAsync(_admin, slug, "general", "doc.txt", "payload");
        (await _admin.PostAsJsonAsync(
            $"/api/v1/teams/{slug}/channels/general/folders",
            new CreateFolderRequest("Docs", null))).EnsureSuccessStatusCode();
        var folderList = await _admin.GetFromJsonAsync<ItemListResponse>($"/api/v1/teams/{slug}/channels/general/items");
        var folderId = folderList!.Items.First(i => i.Kind == ItemKind.Folder).Id;
        (await _admin.PatchAsJsonAsync(
            $"/api/v1/teams/{slug}/channels/general/items/{folderId}",
            new RenameItemRequest("Docs2"))).EnsureSuccessStatusCode();
        (await _admin.PostAsJsonAsync(
            $"/api/v1/teams/{slug}/channels/general/items/{fileId}/move",
            new MoveItemRequest(folderId))).EnsureSuccessStatusCode();
        (await _admin.PatchAsJsonAsync(
            $"/api/v1/teams/{slug}/channels/general/items/{fileId}",
            new RenameItemRequest("doc2.txt"))).EnsureSuccessStatusCode();
        (await _admin.DeleteAsync($"/api/v1/teams/{slug}/channels/general/items/{fileId}")).EnsureSuccessStatusCode();
        (await _admin.PostAsync($"/api/v1/teams/{slug}/channels/general/items/{fileId}/restore", null)).EnsureSuccessStatusCode();
        (await _admin.DeleteAsync($"/api/v1/teams/{slug}/channels/general/items/{folderId}")).EnsureSuccessStatusCode();
        (await _admin.PostAsync($"/api/v1/teams/{slug}/channels/general/items/{folderId}/restore", null)).EnsureSuccessStatusCode();

        var personalId = await UploadPersonalAsync(_admin, "mine.txt", "secret");
        (await _admin.PostAsync($"/api/v1/me/files/items/{personalId}/star", null)).EnsureSuccessStatusCode();
        (await _admin.DeleteAsync($"/api/v1/me/files/items/{personalId}/star")).EnsureSuccessStatusCode();
        (await _admin.PostAsJsonAsync(
            $"/api/v1/me/files/items/{personalId}/copy-to-channel",
            new CopyToChannelRequest(slug, "general", null, "copied.txt"))).EnsureSuccessStatusCode();

        // Personal folder ops for FolderRename/Move already partly covered; ensure personal folder create/rename/move/delete/restore
        var pFolder = await (await _admin.PostAsJsonAsync(
            "/api/v1/me/files/folders",
            new CreateFolderRequest("PDocs", null))).Content.ReadFromJsonAsync<FileItemDto>();
        (await _admin.PatchAsJsonAsync($"/api/v1/me/files/items/{pFolder!.Id}", new RenameItemRequest("PDocs2")))
            .EnsureSuccessStatusCode();
        var pFolder2 = await (await _admin.PostAsJsonAsync(
            "/api/v1/me/files/folders",
            new CreateFolderRequest("Nest", null))).Content.ReadFromJsonAsync<FileItemDto>();
        (await _admin.PostAsJsonAsync($"/api/v1/me/files/items/{pFolder.Id}/move", new MoveItemRequest(pFolder2!.Id)))
            .EnsureSuccessStatusCode();
        (await _admin.DeleteAsync($"/api/v1/me/files/items/{pFolder.Id}")).EnsureSuccessStatusCode();
        (await _admin.PostAsync($"/api/v1/me/files/items/{pFolder.Id}/restore", null)).EnsureSuccessStatusCode();

        // --- Admin catalog extras ---
        (await _admin.PatchAsJsonAsync($"/api/v1/admin/teams/{team.Id}/quota", new UpdateTeamQuotaRequest(50L * 1024 * 1024 * 1024)))
            .EnsureSuccessStatusCode();
        (await _admin.PostAsync($"/api/v1/admin/users/{createdUser!.Id}/disable", null)).EnsureSuccessStatusCode();
        (await _admin.DeleteAsync($"/api/v1/admin/users/{createdUser.Id}")).EnsureSuccessStatusCode();

        var backup = await (await _admin.PostAsync("/api/v1/admin/backups/run", null)).Content.ReadFromJsonAsync<AdminBackupDto>();
        (await _admin.PostAsync($"/api/v1/admin/backups/{backup!.Id}/restore", null)).EnsureSuccessStatusCode();
        (await _admin.PostAsJsonAsync("/api/v1/admin/config", new { key = "feature.x", value = "on" })).EnsureSuccessStatusCode();
        (await _admin.PostAsync($"/api/v1/admin/files/{fileId}/download-audit", null)).EnsureSuccessStatusCode();
        (await _admin.PostAsync($"/api/v1/admin/files/{fileId}/purge", null)).EnsureSuccessStatusCode();

        (await _admin.PostAsync($"/api/v1/teams/{slug}/archive", null)).EnsureSuccessStatusCode();
        // create another team to delete (archived can't delete? DeleteTeam should work for owner)
        var team2 = await (await _admin.PostAsJsonAsync("/api/v1/teams/", new CreateTeamRequest("ToDelete", null)))
            .Content.ReadFromJsonAsync<TeamDto>();
        (await _admin.DeleteAsync($"/api/v1/teams/{team2!.Slug}")).EnsureSuccessStatusCode();

        (await _admin.PostAsync("/api/v1/auth/logout", null)).EnsureSuccessStatusCode();

        await using var verify = _factory.Services.CreateAsyncScope();
        var db = verify.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var seen = await db.AuditEvents.AsNoTracking().Select(a => a.EventType).Distinct().ToListAsync();
        var missing = AuditCatalog.All.Where(t => !seen.Contains(t)).ToList();
        missing.Should().BeEmpty($"missing audit types: {string.Join(", ", missing)}");
    }

    [Fact]
    public async Task Audit_events_reject_update_and_delete()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = await db.AuditEvents.AsNoTracking().OrderByDescending(a => a.OccurredAt).FirstAsync();

        var update = async () => await db.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE audit_events SET "TargetType" = 'hacked' WHERE "Id" = {row.Id}""");
        await update.Should().ThrowAsync<Exception>();

        var delete = async () => await db.Database.ExecuteSqlInterpolatedAsync(
            $"""DELETE FROM audit_events WHERE "Id" = {row.Id}""");
        await delete.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task Non_admin_gets_404_on_admin_dashboard()
    {
        var resp = await _member.GetAsync("/api/v1/admin/dashboard");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Admin_can_list_and_export_audit()
    {
        var list = await _admin.GetAsync("/api/v1/admin/audit?limit=10");
        list.EnsureSuccessStatusCode();
        var json = await _admin.GetAsync("/api/v1/admin/audit/export?format=json");
        json.EnsureSuccessStatusCode();
        var csv = await _admin.GetAsync("/api/v1/admin/audit/export?format=csv");
        csv.EnsureSuccessStatusCode();
        (await csv.Content.ReadAsStringAsync()).Should().Contain("eventType");
    }

    private async Task<Guid> FindUserIdAsync(string email)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        return (await users.FindByEmailAsync(email))!.Id;
    }

    private HttpClient CreateClient() =>
        _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });

    private static async Task LoginAsync(HttpClient client, string email, string password)
    {
        (await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password))).EnsureSuccessStatusCode();
        var csrf = await client.GetAsync("/api/v1/auth/csrf");
        var token = (await csrf.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token);
    }

    private static async Task<Guid> UploadChannelAsync(HttpClient client, string slug, string channel, string name, string text)
    {
        var payload = Encoding.UTF8.GetBytes(text);
        var sha = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        var start = await (await client.PostAsJsonAsync("/api/v1/uploads/", new StartUploadRequest(
            slug, channel, name, payload.Length, "text/plain", null))).Content.ReadFromJsonAsync<StartUploadResponse>();
        using var part = new ByteArrayContent(payload);
        part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        (await client.PutAsync($"/api/v1/uploads/{start!.UploadId}/parts/1", part)).EnsureSuccessStatusCode();
        var file = await (await client.PostAsJsonAsync(
            $"/api/v1/uploads/{start.UploadId}/complete",
            new CompleteUploadRequest(sha, null))).Content.ReadFromJsonAsync<FileItemDto>();
        return file!.Id;
    }

    private static async Task<Guid> UploadPersonalAsync(HttpClient client, string name, string text)
    {
        var payload = Encoding.UTF8.GetBytes(text);
        var sha = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        var start = await (await client.PostAsJsonAsync(
            "/api/v1/me/files/uploads",
            new StartPersonalUploadRequest(name, payload.Length, "text/plain", null)))
            .Content.ReadFromJsonAsync<StartUploadResponse>();
        using var part = new ByteArrayContent(payload);
        part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        (await client.PutAsync($"/api/v1/uploads/{start!.UploadId}/parts/1", part)).EnsureSuccessStatusCode();
        var file = await (await client.PostAsJsonAsync(
            $"/api/v1/uploads/{start.UploadId}/complete",
            new CompleteUploadRequest(sha, null))).Content.ReadFromJsonAsync<FileItemDto>();
        return file!.Id;
    }
}
