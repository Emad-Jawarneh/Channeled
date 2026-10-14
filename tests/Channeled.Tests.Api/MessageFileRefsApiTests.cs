using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Channeled.Application.Chat;
using Channeled.Application.Files;
using Channeled.Application.Teams;
using Channeled.Domain.Teams;
using Channeled.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Channeled.Tests.Api;

[Collection("Api")]
public class MessageFileRefsApiTests : IClassFixture<ChatWorldFixture>
{
    private readonly ChatWorld _w;

    public MessageFileRefsApiTests(ChatWorldFixture fixture) => _w = fixture.World;

    [Fact]
    public async Task Post_with_file_refs_empty_body_and_list_round_trip()
    {
        var talk = await _w.CreateChannelAsync(ChannelKind.Text);
        var files = await _w.CreateChannelAsync(ChannelKind.File);
        var fileId = await UploadTeamFileAsync(_w.Member, files.Name, "shared.txt", "hello file");

        var posted = await PostOkAsync(_w.Member, talk, "", [fileId]);
        posted.FileIds.Should().Equal([fileId]);
        posted.Body.Should().BeEmpty();

        var listed = await _w.ListOkAsync(_w.Viewer, talk);
        listed.Items.Should().ContainSingle(i => i.Id == posted.Id && i.FileIds.SequenceEqual(new[] { fileId }));
    }

    [Fact]
    public async Task Dedupes_and_caps_at_five_file_refs()
    {
        var talk = await _w.CreateChannelAsync(ChannelKind.Text);
        var files = await _w.CreateChannelAsync(ChannelKind.File);
        var ids = new List<Guid>();
        for (var i = 0; i < 6; i++)
        {
            ids.Add(await UploadTeamFileAsync(_w.Member, files.Name, $"f{i}.txt", "x"));
        }

        var tooMany = await PostAsync(_w.Member, talk, "many", ids);
        tooMany.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var five = ids.Take(5).ToList();
        var ok = await PostOkAsync(_w.Member, talk, "five", five.Concat(five).ToList());
        ok.FileIds.Should().Equal(five);
    }

    [Fact]
    public async Task Cross_team_file_rejected()
    {
        var talk = await _w.CreateChannelAsync(ChannelKind.Text);
        var otherTeamFile = await UploadOnOtherTeamAsync("other.txt", "secret");

        var response = await PostAsync(_w.Member, talk, "nope", [otherTeamFile]);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Restricted_file_channel_without_access_rejected()
    {
        var talk = await _w.CreateChannelAsync(ChannelKind.Text);
        var vipFiles = await _w.CreateChannelAsync(ChannelKind.File, ChannelVisibility.Restricted, "vip-files");
        var fileId = await UploadTeamFileAsync(_w.Owner, vipFiles.Name, "secret.bin", "data");

        var response = await PostAsync(_w.Member, talk, "leak?", [fileId]);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Resolve_live_trashed_missing_and_no_leak_for_inaccessible()
    {
        var files = await _w.CreateChannelAsync(ChannelKind.File);
        var vipFiles = await _w.CreateChannelAsync(ChannelKind.File, ChannelVisibility.Restricted, "vip-files");
        var liveId = await UploadTeamFileAsync(_w.Member, files.Name, "live.txt", "live");
        var trashedId = await UploadTeamFileAsync(_w.Member, files.Name, "gone.txt", "gone");
        var secretId = await UploadTeamFileAsync(_w.Owner, vipFiles.Name, "secret.txt", "secret");

        (await _w.Member.Client.DeleteAsync(
            $"/api/v1/teams/{_w.Slug}/channels/{files.Name}/items/{trashedId}")).EnsureSuccessStatusCode();

        var missingId = Guid.NewGuid();
        var resolved = await ResolveAsync(_w.Member, [liveId, trashedId, secretId, missingId]);
        var live = resolved.Items.Single(i => i.FileId == liveId);
        live.Accessible.Should().BeTrue();
        live.State.Should().Be("live");
        live.Name.Should().Be("live.txt");
        live.ChannelName.Should().Be(files.Name);
        live.FolderId.Should().BeNull();

        var trashed = resolved.Items.Single(i => i.FileId == trashedId);
        trashed.Accessible.Should().BeTrue();
        trashed.State.Should().Be("trashed");
        trashed.Name.Should().Be("gone.txt");

        var secret = resolved.Items.Single(i => i.FileId == secretId);
        secret.Accessible.Should().BeFalse();
        secret.Name.Should().BeNull();
        secret.SizeBytes.Should().BeNull();
        secret.ContentType.Should().BeNull();
        secret.State.Should().Be("missing");

        resolved.Items.Single(i => i.FileId == missingId).State.Should().Be("missing");
    }

    [Fact]
    public async Task Message_delete_cascades_file_refs()
    {
        var talk = await _w.CreateChannelAsync(ChannelKind.Text);
        var files = await _w.CreateChannelAsync(ChannelKind.File);
        var fileId = await UploadTeamFileAsync(_w.Member, files.Name, "cascade.txt", "x");
        var msg = await PostOkAsync(_w.Member, talk, "refs", [fileId]);

        (await _w.DeleteAsync(_w.Member, talk, msg.Id)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var scope = _w.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.MessageFileRefs.CountAsync(r => r.MessageId == msg.Id)).Should().Be(0);
    }

    [Fact]
    public async Task SignalR_messageCreated_carries_file_ids_only()
    {
        var talk = await _w.CreateChannelAsync(ChannelKind.Text);
        var files = await _w.CreateChannelAsync(ChannelKind.File);
        var fileId = await UploadTeamFileAsync(_w.Member, files.Name, "hub.txt", "hub");

        await using var hub = await ConnectHubAsync(_w.Viewer);
        var created = hub.Listen<ChannelMessageDto>("messageCreated");
        await hub.Connection.InvokeAsync("JoinChannel", talk.Id);

        var posted = await PostOkAsync(_w.Member, talk, "with file", [fileId]);
        var evt = await created.NextAsync();
        evt.FileIds.Should().Equal([fileId]);
        evt.Id.Should().Be(posted.Id);
    }

    private async Task<HubSession> ConnectHubAsync(ChatWorld.Actor actor)
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
        await connection.StartAsync();
        return new HubSession(connection);
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
        private readonly System.Collections.Concurrent.ConcurrentQueue<T> _items = new();
        private readonly SemaphoreSlim _signal = new(0);

        public void Add(T item)
        {
            _items.Enqueue(item);
            _signal.Release();
        }

        public async Task<T> NextAsync()
        {
            (await _signal.WaitAsync(TimeSpan.FromSeconds(10))).Should().BeTrue();
            _items.TryDequeue(out var item).Should().BeTrue();
            return item!;
        }
    }

    private Task<HttpResponseMessage> PostAsync(ChatWorld.Actor actor, ChannelDto channel, string body, IReadOnlyList<Guid>? fileIds) =>
        actor.Client.PostAsJsonAsync(
            $"/api/v1/teams/{_w.Slug}/channels/{channel.Name}/messages",
            new PostMessageRequest(body, null, null, fileIds));

    private async Task<ChannelMessageDto> PostOkAsync(
        ChatWorld.Actor actor,
        ChannelDto channel,
        string body,
        IReadOnlyList<Guid>? fileIds)
    {
        var response = await PostAsync(actor, channel, body, fileIds);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ChannelMessageDto>())!;
    }

    private async Task<ResolveFilesResponse> ResolveAsync(ChatWorld.Actor actor, IReadOnlyList<Guid> fileIds)
    {
        var response = await actor.Client.PostAsJsonAsync(
            $"/api/v1/teams/{_w.Slug}/files/resolve",
            new ResolveFilesRequest(fileIds));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ResolveFilesResponse>())!;
    }

    private async Task<Guid> UploadTeamFileAsync(ChatWorld.Actor actor, string channelName, string name, string text) =>
        await UploadTeamFileAsync(actor, _w.Slug, channelName, name, text);

    private static async Task<Guid> UploadTeamFileAsync(
        ChatWorld.Actor actor,
        string teamSlug,
        string channelName,
        string name,
        string text)
    {
        var payload = Encoding.UTF8.GetBytes(text);
        var sha = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        var start = await (await actor.Client.PostAsJsonAsync(
            "/api/v1/uploads/",
            new StartUploadRequest(teamSlug, channelName, name, payload.Length, "text/plain", null)))
            .Content.ReadFromJsonAsync<StartUploadResponse>();
        using var part = new ByteArrayContent(payload);
        part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        (await actor.Client.PutAsync($"/api/v1/uploads/{start!.UploadId}/parts/1", part)).EnsureSuccessStatusCode();
        var file = await (await actor.Client.PostAsJsonAsync(
            $"/api/v1/uploads/{start.UploadId}/complete",
            new CompleteUploadRequest(sha, null))).Content.ReadFromJsonAsync<FileItemDto>();
        return file!.Id;
    }

    private async Task<Guid> UploadOnOtherTeamAsync(string name, string text)
    {
        var teamResp = await _w.Outsider.Client.PostAsJsonAsync("/api/v1/teams/", new CreateTeamRequest("Other", null));
        teamResp.EnsureSuccessStatusCode();
        var team = await teamResp.Content.ReadFromJsonAsync<TeamDto>();
        (await _w.Outsider.Client.PostAsJsonAsync(
            $"/api/v1/teams/{team!.Slug}/channels",
            new CreateChannelRequest("files", null, ChannelVisibility.Everyone, ChannelKind.File))).EnsureSuccessStatusCode();
        return await UploadTeamFileAsync(_w.Outsider, team.Slug, "files", name, text);
    }
}
