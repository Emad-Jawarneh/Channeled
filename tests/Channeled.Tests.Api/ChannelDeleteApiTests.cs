using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Channeled.Application.Abstractions.Storage;
using Channeled.Application.Files;
using Channeled.Application.Identity;
using Channeled.Application.Search;
using Channeled.Application.Teams;
using Channeled.Domain.Audit;
using Channeled.Domain.Identity;
using Channeled.Domain.Teams;
using Channeled.Infrastructure.Identity;
using Channeled.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Channeled.Tests.Api;

[Collection("Api")]
public class ChannelDeleteApiTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private HttpClient _owner = null!;
    private HttpClient _member = null!;
    private HttpClient _viewer = null!;
    private string _slug = "";

    public ChannelDeleteApiTests(AuthApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.ResetDatabaseAsync();

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
            await auth.SeedOperatorAsync(new SeedOperatorRequest("del-owner@example.com", "Owner", "OwnerPassword1!"));

            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            foreach (var (email, name) in new[]
                     {
                         ("del-member@example.com", "Member"),
                         ("del-viewer@example.com", "Viewer"),
                     })
            {
                var user = new AppUser
                {
                    Id = UuidV7.NewGuid(),
                    Email = email,
                    UserName = email,
                    DisplayName = name,
                    EmailConfirmed = true,
                    Status = "active",
                };
                (await users.CreateAsync(user, "MemberPassword1!")).Succeeded.Should().BeTrue();
            }
        }

        _owner = CreateClient();
        _member = CreateClient();
        _viewer = CreateClient();
        await LoginAsync(_owner, "del-owner@example.com", "OwnerPassword1!");
        await LoginAsync(_member, "del-member@example.com", "MemberPassword1!");
        await LoginAsync(_viewer, "del-viewer@example.com", "MemberPassword1!");

        var team = await (await _owner.PostAsJsonAsync("/api/v1/teams/", new CreateTeamRequest("Delete Team", null)))
            .Content.ReadFromJsonAsync<TeamDto>();
        _slug = team!.Slug;

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var member = await users.FindByEmailAsync("del-member@example.com");
            var viewer = await users.FindByEmailAsync("del-viewer@example.com");
            db.TeamMembers.AddRange(
                new Channeled.Infrastructure.Teams.TeamMemberEntity
                {
                    TeamId = team.Id,
                    UserId = member!.Id,
                    Role = MemberRole.Member,
                    JoinedAt = DateTimeOffset.UtcNow,
                },
                new Channeled.Infrastructure.Teams.TeamMemberEntity
                {
                    TeamId = team.Id,
                    UserId = viewer!.Id,
                    Role = MemberRole.Viewer,
                    JoinedAt = DateTimeOffset.UtcNow,
                });
            await db.SaveChangesAsync();
        }

        (await _owner.PostAsJsonAsync(
            $"/api/v1/teams/{_slug}/channels",
            new CreateChannelRequest("general", null, ChannelVisibility.Open, ChannelKind.File))).EnsureSuccessStatusCode();
        (await _owner.PostAsJsonAsync(
            $"/api/v1/teams/{_slug}/channels",
            new CreateChannelRequest("archive-me", null, ChannelVisibility.Open, ChannelKind.File))).EnsureSuccessStatusCode();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Discard_deletes_store_object_and_decrements_quota()
    {
        (await _owner.PostAsJsonAsync(
            $"/api/v1/teams/{_slug}/channels",
            new CreateChannelRequest("scratch", null, ChannelVisibility.Open, ChannelKind.File))).EnsureSuccessStatusCode();

        var (file, payload) = await UploadAsync("scratch", "bye.txt");
        var storageKey = await GetStorageKeyAsync(file.Id);

        var before = await _owner.GetFromJsonAsync<TeamStorageDto>($"/api/v1/teams/{_slug}/storage");
        before!.UsageBytes.Should().BeGreaterThanOrEqualTo(payload.Length);

        var del = await _owner.PostAsJsonAsync(
            $"/api/v1/teams/{_slug}/channels/scratch/delete",
            new DeleteChannelRequest(DiscardFiles: true));
        del.EnsureSuccessStatusCode();

        (await _owner.GetAsync($"/api/v1/teams/{_slug}/channels/scratch")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IFileStore>();
            (await store.ExistsAsync(storageKey)).Should().BeFalse();

            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.Files.AnyAsync(f => f.Id == file.Id)).Should().BeFalse();
            (await db.AuditEvents.CountAsync(a => a.EventType == AuditEventType.ChannelDelete))
                .Should().BeGreaterThan(0);
        }

        var after = await _owner.GetFromJsonAsync<TeamStorageDto>($"/api/v1/teams/{_slug}/storage");
        after!.UsageBytes.Should().Be(before.UsageBytes - payload.Length);
    }

    [Fact]
    public async Task Move_preserves_bytes_in_target_channel()
    {
        (await _owner.PostAsJsonAsync(
            $"/api/v1/teams/{_slug}/channels",
            new CreateChannelRequest("source", null, ChannelVisibility.Open, ChannelKind.File))).EnsureSuccessStatusCode();

        var (file, _) = await UploadAsync("source", "keep.txt");
        var storageKey = await GetStorageKeyAsync(file.Id);

        (await _owner.PostAsJsonAsync(
            $"/api/v1/teams/{_slug}/channels/source/delete",
            new DeleteChannelRequest(MoveFilesToChannel: "general"))).EnsureSuccessStatusCode();

        await using var scope = _factory.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IFileStore>();
        (await store.ExistsAsync(storageKey)).Should().BeTrue();

        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var moved = await db.Files.SingleAsync(f => f.Id == file.Id);
        var general = await db.Channels.SingleAsync(c => c.Name == "general");
        moved.ChannelId.Should().Be(general.Id);
        moved.FolderId.Should().BeNull();
    }

    [Fact]
    public async Task File_channel_with_files_requires_disposition()
    {
        (await _owner.PostAsJsonAsync(
            $"/api/v1/teams/{_slug}/channels",
            new CreateChannelRequest("needs-choice", null, ChannelVisibility.Open, ChannelKind.File))).EnsureSuccessStatusCode();
        await UploadAsync("needs-choice", "x.txt");

        var resp = await _owner.PostAsJsonAsync(
            $"/api/v1/teams/{_slug}/channels/needs-choice/delete",
            new DeleteChannelRequest());
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Member_and_viewer_cannot_update_or_delete()
    {
        foreach (var client in new[] { _member, _viewer })
        {
            var put = await client.PutAsJsonAsync(
                $"/api/v1/teams/{_slug}/channels/general",
                new UpdateChannelRequest("hacked", null));
            put.StatusCode.Should().Be(HttpStatusCode.NotFound);

            var del = await client.PostAsJsonAsync(
                $"/api/v1/teams/{_slug}/channels/archive-me/delete",
                new DeleteChannelRequest(DiscardFiles: true));
            del.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
    }

    private async Task<(FileItemDto File, byte[] Payload)> UploadAsync(string channel, string fileName)
    {
        var payload = Encoding.UTF8.GetBytes("channel-delete-" + Guid.NewGuid());
        var sha = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();

        var startResp = await _owner.PostAsJsonAsync("/api/v1/uploads/", new StartUploadRequest(
            _slug, channel, fileName, payload.Length, "text/plain", null));
        startResp.EnsureSuccessStatusCode();
        var start = await startResp.Content.ReadFromJsonAsync<StartUploadResponse>();

        using (var partContent = new ByteArrayContent(payload))
        {
            partContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            (await _owner.PutAsync($"/api/v1/uploads/{start!.UploadId}/parts/1", partContent)).EnsureSuccessStatusCode();
        }

        var idem = "idem-" + Guid.NewGuid().ToString("N");
        using var completeReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/uploads/{start.UploadId}/complete")
        {
            Content = JsonContent.Create(new CompleteUploadRequest(sha, idem)),
        };
        completeReq.Headers.Add("Idempotency-Key", idem);
        var complete = await _owner.SendAsync(completeReq);
        complete.EnsureSuccessStatusCode();
        var file = await complete.Content.ReadFromJsonAsync<FileItemDto>();
        return (file!, payload);
    }

    private async Task<string> GetStorageKeyAsync(Guid fileId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return (await db.Files.AsNoTracking().SingleAsync(f => f.Id == fileId)).StorageKey;
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
        csrf.EnsureSuccessStatusCode();
        var body = await csrf.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", body.GetProperty("token").GetString());
    }
}
