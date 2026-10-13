using System.Net;
using System.Net.Http.Json;
using Channeled.Application.Chat;
using Channeled.Application.Notifications;
using Channeled.Domain.Notifications;
using Channeled.Domain.Teams;
using FluentAssertions;

namespace Channeled.Tests.Api;

[Collection("Api")]
public class ReplyMentionNotificationTests : IClassFixture<ChatWorldFixture>
{
    private readonly ChatWorld _w;

    public ReplyMentionNotificationTests(ChatWorldFixture fixture) => _w = fixture.World;

    [Fact]
    public async Task Reply_and_mention_create_notifications()
    {
        var ch = await _w.CreateChannelAsync(ChannelKind.Text);
        var parent = await _w.PostOkAsync(_w.Owner, ch, "hello team");

        const string memberName = "Chat Member";
        var mentionBody = $"hey @{memberName} — following up";
        var replyResp = await _w.Owner.Client.PostAsJsonAsync(
            $"/api/v1/teams/{_w.Slug}/channels/{ch.Name}/messages",
            new PostMessageRequest(mentionBody, parent.Id, [_w.Member.Id]));
        replyResp.EnsureSuccessStatusCode();
        var reply = await replyResp.Content.ReadFromJsonAsync<ChannelMessageDto>();
        reply!.ReplyTo!.Id.Should().Be(parent.Id);
        reply.Mentions.Should().Contain(m => m.UserId == _w.Member.Id);

        // Owner replied to own message → no reply notification to self; Member gets mention.
        var memberNotes = await _w.Member.Client.GetFromJsonAsync<NotificationListResponse>(
            "/api/v1/notifications?limit=20");
        memberNotes!.Items.Should().Contain(n =>
            n.Type == NotificationType.Mention && n.MessageId == reply.Id);

        // Member replies to owner's message → owner gets Reply notification.
        var memberReplyResp = await _w.Member.Client.PostAsJsonAsync(
            $"/api/v1/teams/{_w.Slug}/channels/{ch.Name}/messages",
            new PostMessageRequest("ack", parent.Id, null));
        memberReplyResp.EnsureSuccessStatusCode();
        var memberReply = await memberReplyResp.Content.ReadFromJsonAsync<ChannelMessageDto>();

        var ownerNotes = await _w.Owner.Client.GetFromJsonAsync<NotificationListResponse>(
            "/api/v1/notifications?unreadOnly=true");
        ownerNotes!.Items.Should().Contain(n =>
            n.Type == NotificationType.Reply && n.MessageId == memberReply!.Id);
        ownerNotes.UnreadCount.Should().BeGreaterThan(0);

        var mark = await _w.Owner.Client.PostAsync(
            $"/api/v1/notifications/{ownerNotes.Items[0].Id}/read", null);
        mark.StatusCode.Should().Be(HttpStatusCode.OK);

        var unread = await _w.Owner.Client.GetFromJsonAsync<UnreadCountResponse>(
            "/api/v1/notifications/unread-count");
        unread!.UnreadCount.Should().BeLessThan(ownerNotes.UnreadCount);
    }
}
