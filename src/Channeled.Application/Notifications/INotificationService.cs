using Channeled.Application.Abstractions.Results;
using Channeled.Application.Teams;

namespace Channeled.Application.Notifications;

public interface INotificationService
{
    Task<AppResult<NotificationListResponse>> ListAsync(Guid userId, int limit, bool unreadOnly, CancellationToken ct = default);

    Task<AppResult<UnreadCountResponse>> UnreadCountAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Total pending team join requests across teams where the user is Admin+ (0 for everyone else).
    /// Computed live from team_join_requests, so it is always accurate regardless of notification read state.
    /// </summary>
    Task<AppResult<PendingJoinCountDto>> GetPendingTeamJoinCountAsync(Guid userId, CancellationToken ct = default);

    Task<AppResult> MarkReadAsync(Guid userId, Guid notificationId, CancellationToken ct = default);

    Task<AppResult> MarkAllReadAsync(Guid userId, CancellationToken ct = default);
}
