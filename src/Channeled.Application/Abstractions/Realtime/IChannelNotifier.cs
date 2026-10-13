namespace Channeled.Application.Abstractions.Realtime;

/// <summary>
/// Broadcasts channel events after DB commit. Failures must not fail the HTTP request.
/// </summary>
public interface IChannelNotifier
{
    void NotifyFilesChanged(Guid channelId);

    void NotifyMessageCreated(Guid channelId, object payload);

    void NotifyMessageDeleted(Guid channelId, object payload);

    void NotifyPresenceChanged(Guid channelId, object payload);

    /// <summary>User-scoped event (e.g. notificationCreated) via hub group <c>user:{id}</c>.</summary>
    void NotifyUser(Guid userId, string method, object payload);

    /// <summary>
    /// Channel visibility or grants changed. Sends <c>channelVisibilityChanged</c> to the channel group, then evicts every
    /// connection from the group; clients re-invoke JoinChannel (which re-authorizes) if they still have access.
    /// </summary>
    void NotifyChannelVisibilityChanged(Guid channelId);
}
