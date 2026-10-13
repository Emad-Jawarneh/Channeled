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
}
