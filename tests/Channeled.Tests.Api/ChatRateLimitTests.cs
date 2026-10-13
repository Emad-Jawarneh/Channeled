using System.Net;
using Channeled.Domain.Teams;
using FluentAssertions;

namespace Channeled.Tests.Api;

[Collection("Api")]
public class ChatRateLimitTests : IClassFixture<RateLimitedChatWorldFixture>
{
    private readonly ChatWorld _w;

    public ChatRateLimitTests(RateLimitedChatWorldFixture fixture) => _w = fixture.World;

    [Fact]
    public async Task Posting_is_rate_limited_per_user_and_reads_are_not()
    {
        var ch = await _w.CreateChannelAsync(ChannelKind.Text);

        for (var i = 0; i < RateLimitedChatFactory.PostsPerMinute; i++)
        {
            (await _w.PostAsync(_w.Member, ch, $"msg {i}")).StatusCode.Should().Be(HttpStatusCode.Created);
        }

        (await _w.PostAsync(_w.Member, ch, "one too many")).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        // Budget is per user: another member still posts, and the limited user can still read and delete.
        (await _w.PostAsync(_w.Admin, ch, "admin is unaffected")).StatusCode.Should().Be(HttpStatusCode.Created);
        var list = await _w.ListOkAsync(_w.Member, ch);
        list.Items.Should().HaveCount(RateLimitedChatFactory.PostsPerMinute + 1);
        (await _w.DeleteAsync(_w.Member, ch, list.Items.Last(i => i.AuthorId == _w.Member.Id).Id))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Rejected posts must not have been stored.
        (await _w.ListOkAsync(_w.Owner, ch)).Items.Should().NotContain(i => i.Body == "one too many");
    }
}
