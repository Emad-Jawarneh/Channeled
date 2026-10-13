using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Channeled.Application.Chat;
using Channeled.Domain.Teams;
using FluentAssertions;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;

namespace Channeled.Tests.Api;

/// <summary>Real SignalR clients (WebSocket) against the in-memory test host.</summary>
[Collection("Api")]
public class ChatHubTests : IClassFixture<ChatWorldFixture>, IAsyncLifetime
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(900);

    private readonly ChatWorld _w;
    private readonly List<HubConnection> _connections = [];

    public ChatHubTests(ChatWorldFixture fixture) => _w = fixture.World;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var c in _connections)
        {
            await c.DisposeAsync();
        }
    }

    [Fact]
    public async Task Join_text_channel_receives_created_and_deleted_after_rest_commit()
    {
        var ch = await _w.CreateChannelAsync(ChannelKind.Text);
        await using var listener = await ConnectAsync(_w.Viewer);
        var created = listener.Listen<ChannelMessageDto>("messageCreated");
        var deleted = listener.Listen<MessageDeletedEvent>("messageDeleted");
        await listener.Connection.InvokeAsync("JoinChannel", ch.Id);

        var posted = await _w.PostOkAsync(_w.Member, ch, "hello hub");
        var evt = await created.NextAsync();
        evt.Id.Should().Be(posted.Id);
        evt.Body.Should().Be("hello hub");
        evt.AuthorDisplayName.Should().Be("Chat Member");

        // The event is only sent after commit: the REST view already contains the row.
        (await _w.ListOkAsync(_w.Viewer, ch)).Items.Should().Contain(i => i.Id == posted.Id);

        (await _w.DeleteAsync(_w.Admin, ch, posted.Id)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var del = await deleted.NextAsync();
        del.MessageId.Should().Be(posted.Id);
        del.ChannelId.Should().Be(ch.Id);
    }

    [Fact]
    public async Task Events_are_scoped_to_the_joined_channel()
    {
        var a = await _w.CreateChannelAsync(ChannelKind.Text);
        var b = await _w.CreateChannelAsync(ChannelKind.Text);
        await using var listener = await ConnectAsync(_w.Viewer);
        var created = listener.Listen<ChannelMessageDto>("messageCreated");
        await listener.Connection.InvokeAsync("JoinChannel", a.Id);

        await _w.PostOkAsync(_w.Member, b, "other channel");
        await created.ExpectNoneAsync(Quiet);

        await _w.PostOkAsync(_w.Member, a, "joined channel");
        (await created.NextAsync()).Body.Should().Be("joined channel");
    }

    [Fact]
    public async Task Outsiders_and_unlisted_members_cannot_join_text_channels()
    {
        var open = await _w.CreateChannelAsync(ChannelKind.Text);
        var vip = await _w.CreateChannelAsync(ChannelKind.Text, ChannelVisibility.Restricted, "vip");

        await using var outsider = await ConnectAsync(_w.Outsider);
        var denied = async () => await outsider.Connection.InvokeAsync("JoinChannel", open.Id);
        (await denied.Should().ThrowAsync<HubException>()).Which.Message.Should().Contain("not_found");

        await using var member = await ConnectAsync(_w.Member);
        var hidden = async () => await member.Connection.InvokeAsync("JoinChannel", vip.Id);
        (await hidden.Should().ThrowAsync<HubException>()).Which.Message.Should().Contain("not_found");

        await _w.SetChannelMemberAsync(vip, _w.Member, canWrite: false);
        await member.Connection.InvokeAsync("JoinChannel", vip.Id); // now listed: allowed (read-only is fine)
    }

    [Fact]
    public async Task Presence_is_broadcast_on_first_join_and_removed_only_when_last_tab_closes()
    {
        var ch = await _w.CreateChannelAsync(ChannelKind.Text);

        await using var observer = await ConnectAsync(_w.Owner);
        var presence = observer.Listen<PresenceChangedEvent>("presenceChanged");
        await observer.Connection.InvokeAsync("JoinChannel", ch.Id);
        (await presence.NextAsync()).UserId.Should().Be(_w.Owner.Id); // observer sees itself come online
        presence.Drain();

        // Member opens tab A -> online broadcast.
        await using var tabA = await ConnectAsync(_w.Member);
        await tabA.Connection.InvokeAsync("JoinChannel", ch.Id);
        var online = await presence.NextAsync();
        online.UserId.Should().Be(_w.Member.Id);
        online.Online.Should().BeTrue();
        online.DisplayName.Should().Be("Chat Member");

        // Tab B for the SAME user: not a new presence (no broadcast).
        await using var tabB = await ConnectAsync(_w.Member);
        await tabB.Connection.InvokeAsync("JoinChannel", ch.Id);
        await presence.ExpectNoneAsync(Quiet);

        var snapshot = await _w.Owner.Client.GetFromJsonAsync<PresenceListResponse>(
            $"/api/v1/teams/{_w.Slug}/channels/{ch.Name}/presence");
        snapshot!.Users.Select(u => u.UserId).Should().BeEquivalentTo([_w.Owner.Id, _w.Member.Id]);

        // Closing tab A leaves tab B -> still online, no event.
        await tabA.Connection.StopAsync();
        await presence.ExpectNoneAsync(Quiet);
        (await _w.Owner.Client.GetFromJsonAsync<PresenceListResponse>(
                $"/api/v1/teams/{_w.Slug}/channels/{ch.Name}/presence"))!
            .Users.Select(u => u.UserId).Should().Contain(_w.Member.Id);

        // Closing the LAST tab removes presence and broadcasts offline.
        await tabB.Connection.StopAsync();
        var offline = await presence.NextAsync();
        offline.UserId.Should().Be(_w.Member.Id);
        offline.Online.Should().BeFalse();
        (await _w.Owner.Client.GetFromJsonAsync<PresenceListResponse>(
                $"/api/v1/teams/{_w.Slug}/channels/{ch.Name}/presence"))!
            .Users.Select(u => u.UserId).Should().NotContain(_w.Member.Id);
    }

    [Fact]
    public async Task LeaveChannel_on_last_connection_removes_presence()
    {
        var ch = await _w.CreateChannelAsync(ChannelKind.Text);
        await using var observer = await ConnectAsync(_w.Owner);
        var presence = observer.Listen<PresenceChangedEvent>("presenceChanged");
        await observer.Connection.InvokeAsync("JoinChannel", ch.Id);
        await presence.NextAsync();
        presence.Drain();

        await using var member = await ConnectAsync(_w.Member);
        await member.Connection.InvokeAsync("JoinChannel", ch.Id);
        (await presence.NextAsync()).Online.Should().BeTrue();

        await member.Connection.InvokeAsync("LeaveChannel", ch.Id);
        var offline = await presence.NextAsync();
        offline.UserId.Should().Be(_w.Member.Id);
        offline.Online.Should().BeFalse();
    }

    [Fact]
    public async Task Presence_is_not_tracked_for_file_channels()
    {
        var files = await _w.CreateChannelAsync(ChannelKind.File);
        await using var observer = await ConnectAsync(_w.Owner);
        var presence = observer.Listen<PresenceChangedEvent>("presenceChanged");
        await observer.Connection.InvokeAsync("JoinChannel", files.Id);

        await using var member = await ConnectAsync(_w.Member);
        await member.Connection.InvokeAsync("JoinChannel", files.Id); // joining for filesChanged is still allowed
        await presence.ExpectNoneAsync(Quiet);

        await member.Connection.StopAsync();
        await presence.ExpectNoneAsync(Quiet);
    }

    // ---------- plumbing ----------

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

    private sealed class HubSession(HubConnection connection) : IAsyncDisposable
    {
        public HubConnection Connection { get; } = connection;

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

        public void Drain()
        {
            while (_signal.Wait(0))
            {
                _items.TryDequeue(out _);
            }
        }
    }
}
