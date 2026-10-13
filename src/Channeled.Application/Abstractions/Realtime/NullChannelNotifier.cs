namespace Channeled.Application.Abstractions.Realtime;

public sealed class NullChannelNotifier : IChannelNotifier
{
    public void NotifyFilesChanged(Guid channelId)
    {
    }

    public void NotifyMessageCreated(Guid channelId, object payload)
    {
    }

    public void NotifyMessageDeleted(Guid channelId, object payload)
    {
    }

    public void NotifyPresenceChanged(Guid channelId, object payload)
    {
    }
}

public sealed class NullAuthStateChanged : IAuthStateChanged
{
    public void NotifyUser(Guid userId)
    {
    }

    public void NotifyChannel(Guid channelId)
    {
    }

    public void NotifyTeam(Guid teamId)
    {
    }
}
