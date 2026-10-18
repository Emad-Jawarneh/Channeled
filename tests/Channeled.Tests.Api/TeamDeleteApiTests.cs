using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Channeled.Application.Abstractions.Operations;
using Channeled.Application.Abstractions.Storage;
using Channeled.Application.Files;
using Channeled.Application.Identity;
using Channeled.Application.Search;
using Channeled.Application.Teams;
using Channeled.Domain.Audit;
using Channeled.Domain.Errors;
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
public class TeamDeleteApiTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private HttpClient _owner = null!;
    private string _slug = "";
    private Guid _teamId;

    public TeamDeleteApiTests(AuthApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.ResetDatabaseAsync();
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
            await auth.SeedOperatorAsync(new SeedOperatorRequest("td-owner@example.com", "Owner", "OwnerPassword1!"));
        }

        _owner = CreateClient();
        await LoginAsync(_owner, "td-owner@example.com", "OwnerPassword1!");

        var team = await (await _owner.PostAsJsonAsync("/api/v1/teams/", new CreateTeamRequest("Team Delete", null)))
            .Content.ReadFromJsonAsync<TeamDto>();
        _slug = team!.Slug;
        _teamId = team.Id;

        (await _owner.PostAsJsonAsync(
            $"/api/v1/teams/{_slug}/channels",
            new CreateChannelRequest("general", null, ChannelVisibility.Everyone, ChannelKind.File))).EnsureSuccessStatusCode();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Discard_deletes_files_bytes_and_writes_surviving_audit()
    {
        var (file, payload) = await UploadAsync("general", "gone.txt");
        var storageKey = await GetStorageKeyAsync(file.Id);

        var del = await _owner.PostAsJsonAsync(
            $"/api/v1/teams/{_slug}/delete",
            new DeleteTeamRequest(ConfirmDiscard: true, Password: "OwnerPassword1!"));
        del.EnsureSuccessStatusCode();

        (await _owner.GetAsync($"/api/v1/teams/{_slug}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        await DrainObjectDeleteJobsAsync();

        await using var scope = _factory.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IFileStore>();
        (await store.ExistsAsync(storageKey)).Should().BeFalse();

        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.Teams.AnyAsync(t => t.Id == _teamId)).Should().BeFalse();
        (await db.Files.AnyAsync(f => f.Id == file.Id)).Should().BeFalse();
        (await db.Notifications.CountAsync(n => n.TeamId == _teamId)).Should().Be(0);

        var audit = await db.AuditEvents.SingleAsync(a => a.EventType == AuditEventType.TeamDelete && a.TargetId == _teamId.ToString());
        audit.TeamId.Should().BeNull();
        using var doc = JsonDocument.Parse(audit.DetailsJson);
        doc.RootElement.GetProperty("fileCount").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        doc.RootElement.GetProperty("byteCount").GetInt64().Should().BeGreaterThanOrEqualTo(payload.Length);
        doc.RootElement.GetProperty("teamName").GetString().Should().Be("Team Delete");

        var reconcile = scope.ServiceProvider.GetRequiredService<IStoreReconciliationService>();
        var report = await reconcile.ReconcileAsync();
        report.IsClean.Should().BeTrue();
    }

    [Fact]
    public async Task Requires_confirmDiscard_and_password()
    {
        var noConfirm = await _owner.PostAsJsonAsync(
            $"/api/v1/teams/{_slug}/delete",
            new DeleteTeamRequest(ConfirmDiscard: false, Password: "OwnerPassword1!"));
        noConfirm.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var badPw = await _owner.PostAsJsonAsync(
            $"/api/v1/teams/{_slug}/delete",
            new DeleteTeamRequest(ConfirmDiscard: true, Password: "wrong-password!!"));
        badPw.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await badPw.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be(ErrorCodes.InvalidPassword);

        (await _owner.GetAsync($"/api/v1/teams/{_slug}")).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Rejects_when_team_file_count_exceeds_cap()
    {
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var channel = await db.Channels.SingleAsync(c => c.TeamId == _teamId && c.Name == "general");
            var now = DateTimeOffset.UtcNow;
            for (var i = 0; i < 101; i++)
            {
                db.Files.Add(new Channeled.Infrastructure.Files.FileEntity
                {
                    Id = UuidV7.NewGuid(),
                    ChannelId = channel.Id,
                    Name = $"f{i}.txt",
                    SizeBytes = 1,
                    ContentType = "text/plain",
                    StorageKey = $"objects/00/00/{Guid.NewGuid():N}",
                    Sha256 = new string('a', 64),
                    UploadedBy = Guid.Empty,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            }

            await db.SaveChangesAsync();
        }

        var resp = await _owner.PostAsJsonAsync(
            $"/api/v1/teams/{_slug}/delete",
            new DeleteTeamRequest(ConfirmDiscard: true, Password: "OwnerPassword1!"));
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be(ErrorCodes.TeamDeleteCapExceeded);

        (await _owner.GetAsync($"/api/v1/teams/{_slug}")).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Join_after_delete_fails_and_leaves_zero_team_notifications()
    {
        var link = await _owner.GetFromJsonAsync<TeamJoinLinkDto>($"/api/v1/teams/{_slug}/join-link");
        var token = link!.Token;

        // Seed a dangling notification that would otherwise survive a naive team drop.
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var owner = await users.FindByEmailAsync("td-owner@example.com");
            db.Notifications.Add(new Channeled.Infrastructure.Notifications.NotificationEntity
            {
                Id = UuidV7.NewGuid(),
                UserId = owner!.Id,
                Type = Channeled.Domain.Notifications.NotificationType.TeamJoinRequest,
                TeamId = _teamId,
                ChannelId = Guid.Empty,
                MessageId = Guid.Empty,
                ActorId = owner.Id,
                BodyPreview = "pending",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        (await _owner.PostAsJsonAsync(
            $"/api/v1/teams/{_slug}/delete",
            new DeleteTeamRequest(ConfirmDiscard: true, Password: "OwnerPassword1!"))).EnsureSuccessStatusCode();

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.Notifications.CountAsync(n => n.TeamId == _teamId)).Should().Be(0);
        }

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var outsider = new AppUser
            {
                Id = UuidV7.NewGuid(),
                Email = "td-outsider@example.com",
                UserName = "td-outsider@example.com",
                DisplayName = "Outsider",
                UsernameSlug = "tdoutsider",
                EmailConfirmed = true,
                Status = "active",
            };
            (await users.CreateAsync(outsider, "OutsiderPassword1!")).Succeeded.Should().BeTrue();
        }

        using var outsiderClient = CreateClient();
        await LoginAsync(outsiderClient, "td-outsider@example.com", "OutsiderPassword1!");
        var join = await outsiderClient.PostAsync($"/api/v1/teams/join/{token}", null);
        join.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.BadRequest);
        (await outsiderClient.GetAsync($"/api/v1/teams/{_slug}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Upload_after_team_delete_returns_not_found()
    {
        (await _owner.PostAsJsonAsync(
            $"/api/v1/teams/{_slug}/delete",
            new DeleteTeamRequest(ConfirmDiscard: true, Password: "OwnerPassword1!"))).EnsureSuccessStatusCode();

        var start = await _owner.PostAsJsonAsync("/api/v1/uploads/", new StartUploadRequest(
            _slug, "general", "late.txt", 3, "text/plain", null));
        start.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Just_under_team_cap_succeeds()
    {
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var channel = await db.Channels.SingleAsync(c => c.TeamId == _teamId && c.Name == "general");
            var now = DateTimeOffset.UtcNow;
            for (var i = 0; i < 100; i++)
            {
                db.Files.Add(new Channeled.Infrastructure.Files.FileEntity
                {
                    Id = UuidV7.NewGuid(),
                    ChannelId = channel.Id,
                    Name = $"u{i}.txt",
                    SizeBytes = 1,
                    ContentType = "text/plain",
                    StorageKey = $"objects/00/01/{Guid.NewGuid():N}",
                    Sha256 = new string('b', 64),
                    UploadedBy = Guid.Empty,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            }

            await db.SaveChangesAsync();
        }

        (await _owner.PostAsJsonAsync(
            $"/api/v1/teams/{_slug}/delete",
            new DeleteTeamRequest(ConfirmDiscard: true, Password: "OwnerPassword1!"))).EnsureSuccessStatusCode();

        await using var check = _factory.Services.CreateAsyncScope();
        var db2 = check.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db2.Teams.AnyAsync(t => t.Id == _teamId)).Should().BeFalse();
        (await db2.Files.CountAsync(f => f.ChannelId == null)).Should().Be(0);
    }

    private async Task<(FileItemDto File, byte[] Payload)> UploadAsync(string channel, string fileName)
    {
        var payload = Encoding.UTF8.GetBytes("team-delete-" + Guid.NewGuid());
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

        var idem = Guid.NewGuid().ToString("N");
        var completeReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/uploads/{start.UploadId}/complete")
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
        return (await db.Files.SingleAsync(f => f.Id == fileId)).StorageKey;
    }

    private async Task DrainObjectDeleteJobsAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<Channeled.Infrastructure.Jobs.IObjectDeleteJobProcessor>();
        for (var i = 0; i < 20; i++)
        {
            var n = await processor.ProcessPendingAsync(50);
            if (n == 0) break;
        }
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
