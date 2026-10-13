namespace Channeled.Application.Abstractions.Realtime;

/// <summary>
/// Disconnects hub connections whose authorization may have changed.
/// </summary>
public interface IAuthStateChanged
{
    void NotifyUser(Guid userId);

    void NotifyChannel(Guid channelId);

    void NotifyTeam(Guid teamId);
}
