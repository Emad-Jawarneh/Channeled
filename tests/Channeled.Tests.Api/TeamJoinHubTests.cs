using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using Channeled.Application.Abstractions.Realtime;
using Channeled.Application.Chat;
using Channeled.Application.Teams;
using Channeled.Domain.Identity;
using Channeled.Domain.Teams;
using Channeled.Infrastructure.Identity;
using Channeled.Infrastructure.Persistence;
using Channeled.Infrastructure.Teams;
using FluentAssertions;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Channeled.Tests.Api;

/// <summary>Realtime consequences of team privacy changes (real WebSocket SignalR clients, ChatHubTests style).</summary>
[Collection("Api")]
public class TeamJoinHubTests : IClassFixture<ChatWorldFixture>, IAsyncLifetime
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(900);

    /// <summary>
    /// Demotion/removal aborts the user's connections immediately; the periodic sweep
    /// (<see cref="RealtimeOptions.AuthStateSweepSeconds"/>, default 10 s) is the fallback. One sweep plus slack.
    /// </summary>
    private static readonly TimeSpan AuthStateBound = TimeSpan.FromSeconds(new RealtimeOptions().AuthStateSweepSeconds + 5);

    private readonly ChatWorld _w;
    private readonly List<HubConnection> _connections = [];

    public TeamJoinHubTests(ChatWorldFixture fixture) => _w = fixture.World;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var c in _connections)
        {
            await c.DisposeAsync();
        }
    }

    [Fact]
    public async Task Visibility_flip_drops_the_channel_group_and_only_authorized_clients_can_rejoin()
    {
        var ch = await _w.CreateChannelAsync(ChannelKind.Text);
        await _w.SetChannelMemberAsync(ch, _w.Viewer, canWrite: false); // dormant grant: survives restriction

        await using var viewer = await ConnectAsync(_w.Viewer);
        await using var member = await ConnectAsync(_w.Member);
        var viewerCreated = viewer.Listen<ChannelMessageDto>("messageCreated");
        var viewerFlipped = viewer.Listen<JsonElement>("channelVisibilityChanged");
        await viewer.Connection.InvokeAsync("JoinChannel", ch.Id);
        await member.Connection.InvokeAsync("JoinChannel", ch.Id);

        await _w.PostOkAsync(_w.Owner, ch, "before flip");
        (await viewerCreated.NextAsync()).Body.Should().Be("before flip");

        await SetVisibilityAsync(ch, ChannelVisibility.Restricted);

        // The granted Viewer keeps access but is told to rejoin ...
        (await viewerFlipped.NextAsync()).GetProperty("channelId").GetGuid().Should().Be(ch.Id);
        // ... while the Member without a grant lost access altogether: connection aborted by AuthStateChanged.
        await member.WhenClosed.WaitAsync(AuthStateBound);

        // The Viewer is no longer in the SignalR group: nothing arrives until it rejoins.
        await _w.PostOkAsync(_w.Owner, ch, "after flip");
        await viewerCreated.ExpectNoneAsync(Quiet);

        await viewer.Connection.InvokeAsync("JoinChannel", ch.Id); // re-authorized against live grants
        await _w.PostOkAsync(_w.Owner, ch, "after rejoin");
        (await viewerCreated.NextAsync()).Body.Should().Be("after rejoin");

        await using var memberAgain = await ConnectAsync(_w.Member);
        var denied = async () => await memberAgain.Connection.InvokeAsync("JoinChannel", ch.Id);
        (await denied.Should().ThrowAsync<HubException>()).Which.Message.Should().Contain("not_found");

        // Flipping back also evicts the group: clients must rejoin after every visibility change.
        await SetVisibilityAsync(ch, ChannelVisibility.Everyone);
        (await viewerFlipped.NextAsync()).GetProperty("channelId").GetGuid().Should().Be(ch.Id);
        await _w.PostOkAsync(_w.Owner, ch, "after revert");
        await viewerCreated.ExpectNoneAsync(Quiet);
        await memberAgain.Connection.InvokeAsync("JoinChannel", ch.Id); // open again: allowed
    }

    [Fact]
    public async Task Demoted_admin_stops_receiving_restricted_channel_events_within_the_auth_state_bound()
    {
        var ch = await _w.CreateChannelAsync(ChannelKind.Text, ChannelVisibility.Restricted, "boardroom");
        var demotable = await CreateAdminAsync($"demotable-{Guid.NewGuid():N}@example.com");

        await using var control = await ConnectAsync(_w.Admin); // an Admin who stays Admin
        await using var goes = await ConnectAsync(demotable.Actor);
        var controlCreated = control.Listen<ChannelMessageDto>("messageCreated");
        var goesCreated = goes.Listen<ChannelMessageDto>("messageCreated");
        await control.Connection.InvokeAsync("JoinChannel", ch.Id); // Admins see Restricted channels implicitly
        await goes.Connection.InvokeAsync("JoinChannel", ch.Id);

        await _w.PostOkAsync(_w.Owner, ch, "pre-demotion");
        (await controlCreated.NextAsync()).Body.Should().Be("pre-demotion");
        (await goesCreated.NextAsync()).Body.Should().Be("pre-demotion");

        var demote = await _w.Owner.Client.PutAsJsonAsync(
            $"/api/v1/teams/{_w.Slug}/members/{demotable.Actor.Id}/role",
            new ChangeMemberRoleRequest(MemberRole.Member));
        demote.EnsureSuccessStatusCode();
        var started = DateTime.UtcNow;
        await goes.WhenClosed.WaitAsync(AuthStateBound);
        (DateTime.UtcNow - started).Should().BeLessThan(AuthStateBound);

        await _w.PostOkAsync(_w.Owner, ch, "post-demotion");
        (await controlCreated.NextAsync()).Body.Should().Be("post-demotion");
        await goesCreated.ExpectNoneAsync(Quiet);

        await using var again = await ConnectAsync(demotable.Actor);
        var denied = async () => await again.Connection.InvokeAsync("JoinChannel", ch.Id);
        (await denied.Should().ThrowAsync<HubException>()).Which.Message.Should().Contain("not_found");
    }

    // ---------- plumbing ----------

    private async Task SetVisibilityAsync(ChannelDto channel, ChannelVisibility visibility)
    {
        var resp = await _w.Owner.Client.PutAsJsonAsync(
            $"/api/v1/teams/{_w.Slug}/channels/{channel.Name}/visibility",
            new SetChannelVisibilityRequest(visibility));
        resp.EnsureSuccessStatusCode();
    }

    private async Task<(ChatWorld.Actor Actor, Guid Id)> CreateAdminAsync(string email)
    {
        await using (var scope = _w.Factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = new AppUser
            {
                Id = UuidV7.NewGuid(),
                Email = email,
                UserName = email,
                DisplayName = "Demotable Admin",
                EmailConfirmed = true,
                Status = "active",
            };
            (await users.CreateAsync(user, ChatWorld.Password)).Succeeded.Should().BeTrue();

            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.TeamMembers.Add(new TeamMemberEntity
            {
                TeamId = _w.TeamId,
                UserId = user.Id,
                Role = MemberRole.Admin,
                JoinedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var actor = await _w.LoginAsync(email);
        return (actor, actor.Id);
    }

    private async Task<HubSession> ConnectAsync(ChatWorld.Actor actor)
    {
        var server = _w.Factory.Server;
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri("http://localhost/hubs/channel"), options =>
            {
                options.Transports = HttpTransportType.WebSockets;
                options.SkipNegotiation = true;
                options.WebSocketFactory = async (context, ct) =>
                {
                    var ws = server.CreateWebSocketClient();
                    ws.ConfigureRequest = request =>
                    {
                        request.Headers.Cookie = actor.Cookie;
                        request.Headers.Origin = "http://localhost:5173";
                    };
                    return await ws.ConnectAsync(context.Uri, ct);
                };
            })
            .Build();
        _connections.Add(connection);
        var session = new HubSession(connection);
        await connection.StartAsync();
        return session;
    }

    private sealed class HubSession : IAsyncDisposable
    {
        private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public HubSession(HubConnection connection)
        {
            Connection = connection;
            connection.Closed += _ =>
            {
                _closed.TrySetResult();
                return Task.CompletedTask;
            };
        }

        public HubConnection Connection { get; }

        /// <summary>Completes when the server (or client) closes the connection.</summary>
        public Task WhenClosed => _closed.Task;

        public EventQueue<T> Listen<T>(string method)
        {
            var queue = new EventQueue<T>();
            Connection.On<T>(method, queue.Add);
            return queue;
        }

        public async ValueTask DisposeAsync() => await Connection.DisposeAsync();
    }

    private sealed class EventQueue<T>
    {
        private readonly ConcurrentQueue<T> _items = new();
        private readonly SemaphoreSlim _signal = new(0);

        public void Add(T item)
        {
            _items.Enqueue(item);
            _signal.Release();
        }

        public async Task<T> NextAsync()
        {
            if (!await _signal.WaitAsync(Wait))
            {
                throw new TimeoutException($"No {typeof(T).Name} event within {Wait}.");
            }

            _items.TryDequeue(out var item).Should().BeTrue();
            return item!;
        }

        public async Task ExpectNoneAsync(TimeSpan window)
        {
            (await _signal.WaitAsync(window)).Should().BeFalse("no {0} event expected", typeof(T).Name);
        }
    }
}
