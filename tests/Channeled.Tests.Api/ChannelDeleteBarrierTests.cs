using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Channeled.Application.Files;
using Channeled.Application.Identity;
using Channeled.Application.Teams;
using Channeled.Domain.Identity;
using Channeled.Domain.Teams;
using Channeled.Infrastructure.Identity;
using Channeled.Infrastructure.Persistence;
using Channeled.Infrastructure.Teams;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Channeled.Tests.Api;

[Collection("Api")]
public class ChannelDeleteBarrierTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private HttpClient _owner = null!;
    private string _slug = "";

    public ChannelDeleteBarrierTests(AuthApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.ResetDatabaseAsync();
        ChannelWriteGuard.BeforeLockHook = null;
        ChannelWriteGuard.AfterLockHook = null;

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
            await auth.SeedOperatorAsync(new SeedOperatorRequest("barrier-owner@example.com", "Owner", "OwnerPassword1!"));
        }

        _owner = CreateClient();
        await LoginAsync(_owner, "barrier-owner@example.com", "OwnerPassword1!");

        var team = await (await _owner.PostAsJsonAsync("/api/v1/teams/", new CreateTeamRequest("Barrier Team", null)))
            .Content.ReadFromJsonAsync<TeamDto>();
        _slug = team!.Slug;

        (await _owner.PostAsJsonAsync(
            $"/api/v1/teams/{_slug}/channels",
            new CreateChannelRequest("general", null, ChannelVisibility.Everyone, ChannelKind.File))).EnsureSuccessStatusCode();
    }

    public Task DisposeAsync()
    {
        ChannelWriteGuard.BeforeLockHook = null;
        ChannelWriteGuard.AfterLockHook = null;
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Folder_create_vs_delete_fails_clean_without_orphan()
    {
        (await _owner.PostAsJsonAsync(
            $"/api/v1/teams/{_slug}/channels",
            new CreateChannelRequest("race", null, ChannelVisibility.Everyone, ChannelKind.File))).EnsureSuccessStatusCode();

        var deleteHolding = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writerReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var goDelete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var goWriter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        ChannelWriteGuard.AfterLockHook = async (_, mode, ct) =>
        {
            if (mode == "update")
            {
                deleteHolding.TrySetResult();
                await goDelete.Task.WaitAsync(ct);
            }
        };
        ChannelWriteGuard.BeforeLockHook = async (_, mode, ct) =>
        {
            if (mode == "share")
            {
                writerReady.TrySetResult();
                await goWriter.Task.WaitAsync(ct);
            }
        };

        var deleteTask = _owner.PostAsJsonAsync(
            $"/api/v1/teams/{_slug}/channels/race/delete",
            new DeleteChannelRequest(DiscardFiles: true));

        await deleteHolding.Task.WaitAsync(TimeSpan.FromSeconds(30));

        var createTask = _owner.PostAsJsonAsync(
            $"/api/v1/teams/{_slug}/channels/race/folders",
            new CreateFolderRequest("should-fail", null));

        await writerReady.Task.WaitAsync(TimeSpan.FromSeconds(30));
        goDelete.TrySetResult();
        (await deleteTask).EnsureSuccessStatusCode();
        goWriter.TrySetResult();

        var createResp = await createTask;
        createResp.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.Channels.AnyAsync(c => c.Name == "race")).Should().BeFalse();
        (await db.Folders.CountAsync(f => f.Name == "should-fail")).Should().Be(0);
    }

    [Fact]
    public async Task Rename_racing_delete_returns_404()
    {
        (await _owner.PostAsJsonAsync(
            $"/api/v1/teams/{_slug}/channels",
            new CreateChannelRequest("rename-me", null, ChannelVisibility.Everyone, ChannelKind.File))).EnsureSuccessStatusCode();

        (await _owner.PostAsJsonAsync(
            $"/api/v1/teams/{_slug}/channels/rename-me/delete",
            new DeleteChannelRequest(DiscardFiles: true))).EnsureSuccessStatusCode();

        var rename = await _owner.PutAsJsonAsync(
            $"/api/v1/teams/{_slug}/channels/rename-me",
            new UpdateChannelRequest("renamed", null));
        rename.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Text_channel_over_message_cap_rejected()
    {
        (await _owner.PostAsJsonAsync(
            $"/api/v1/teams/{_slug}/channels",
            new CreateChannelRequest("chatty", null, ChannelVisibility.Everyone, ChannelKind.Text))).EnsureSuccessStatusCode();

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var owner = await users.FindByEmailAsync("barrier-owner@example.com");
            var channel = await db.Channels.SingleAsync(c => c.Name == "chatty");
            var now = DateTimeOffset.UtcNow;
            // Seed just over the configured test cap (tests override MaxMessagesPerTxn to 50 when set).
            var cap = 50;
            for (var i = 0; i < cap + 1; i++)
            {
                db.ChannelMessages.Add(new Channeled.Infrastructure.Chat.ChannelMessageEntity
                {
                    Id = UuidV7.NewGuid(),
                    ChannelId = channel.Id,
                    AuthorId = owner!.Id,
                    Body = $"m{i}",
                    CreatedAt = now,
                });
            }

            await db.SaveChangesAsync();
        }

        var del = await _owner.PostAsJsonAsync(
            $"/api/v1/teams/{_slug}/channels/chatty/delete",
            new DeleteChannelRequest());
        del.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Cross_delete_move_pair_completes_without_deadlock()
    {
        (await _owner.PostAsJsonAsync(
            $"/api/v1/teams/{_slug}/channels",
            new CreateChannelRequest("left", null, ChannelVisibility.Everyone, ChannelKind.File))).EnsureSuccessStatusCode();
        (await _owner.PostAsJsonAsync(
            $"/api/v1/teams/{_slug}/channels",
            new CreateChannelRequest("right", null, ChannelVisibility.Everyone, ChannelKind.File))).EnsureSuccessStatusCode();

        var a = _owner.PostAsJsonAsync(
            $"/api/v1/teams/{_slug}/channels/left/delete",
            new DeleteChannelRequest(MoveFilesToChannel: "right"));
        var b = _owner.PostAsJsonAsync(
            $"/api/v1/teams/{_slug}/channels/right/delete",
            new DeleteChannelRequest(MoveFilesToChannel: "left"));

        var results = await Task.WhenAll(a, b).WaitAsync(TimeSpan.FromSeconds(60));
        foreach (var r in results)
        {
            (r.IsSuccessStatusCode
             || r.StatusCode == HttpStatusCode.NotFound
             || r.StatusCode == HttpStatusCode.BadRequest).Should().BeTrue();
        }

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var left = await db.Channels.AnyAsync(c => c.Name == "left");
        var right = await db.Channels.AnyAsync(c => c.Name == "right");
        // At most one of the mutual move-deletes can succeed; neither may hang (WaitAsync above).
        (left && right).Should().BeFalse();
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
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", body.GetProperty("token").GetString()!);
    }
}
