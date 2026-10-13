using Channeled.Infrastructure.Realtime;
using FluentAssertions;

namespace Channeled.Tests.Unit.Realtime;

public class PresenceTrackerTests
{
    private readonly Guid _channel = Guid.NewGuid();
    private readonly Guid _user = Guid.NewGuid();

    [Fact]
    public void First_connection_is_new_presence_second_is_not()
    {
        var tracker = new PresenceTracker();
        tracker.Join(_channel, _user, "Ada", "tab-a").Should().BeTrue();
        tracker.Join(_channel, _user, "Ada", "tab-b").Should().BeFalse();
        tracker.Online(_channel).Should().ContainSingle(u => u.UserId == _user && u.DisplayName == "Ada");
    }

    [Fact]
    public void Joining_the_same_channel_twice_from_one_connection_is_idempotent()
    {
        var tracker = new PresenceTracker();
        tracker.Join(_channel, _user, "Ada", "tab-a").Should().BeTrue();
        tracker.Join(_channel, _user, "Ada", "tab-a").Should().BeFalse();
        tracker.Leave(_channel, "tab-a")!.Online.Should().BeFalse();
        tracker.Online(_channel).Should().BeEmpty();
    }

    [Fact]
    public void Offline_only_when_last_connection_leaves()
    {
        var tracker = new PresenceTracker();
        tracker.Join(_channel, _user, "Ada", "tab-a");
        tracker.Join(_channel, _user, "Ada", "tab-b");

        tracker.Leave(_channel, "tab-a").Should().BeNull();
        tracker.Online(_channel).Should().HaveCount(1);

        var offline = tracker.Leave(_channel, "tab-b");
        offline.Should().NotBeNull();
        offline!.Online.Should().BeFalse();
        offline.UserId.Should().Be(_user);
        tracker.Online(_channel).Should().BeEmpty();
    }

    [Fact]
    public void Disconnect_removes_connection_from_every_channel_and_reports_only_users_gone_offline()
    {
        var tracker = new PresenceTracker();
        var other = Guid.NewGuid();
        tracker.Join(_channel, _user, "Ada", "tab-a");
        tracker.Join(other, _user, "Ada", "tab-a");
        tracker.Join(other, _user, "Ada", "tab-b"); // second tab only in the other channel

        var offline = tracker.LeaveAll("tab-a");

        offline.Should().ContainSingle(e => e.ChannelId == _channel && !e.Online);
        tracker.Online(_channel).Should().BeEmpty();
        tracker.Online(other).Should().HaveCount(1);
        tracker.LeaveAll("tab-a").Should().BeEmpty("a repeated disconnect is a no-op");
    }

    [Fact]
    public void Leaving_a_channel_that_was_never_joined_is_a_no_op()
    {
        new PresenceTracker().Leave(_channel, "ghost").Should().BeNull();
    }
}
