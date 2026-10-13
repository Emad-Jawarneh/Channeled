using Channeled.Application.Abstractions.Results;

namespace Channeled.Application.Notifications;

public interface INotificationService
{
    Task<AppResult<NotificationListResponse>> ListAsync(Guid userId, int limit, bool unreadOnly, CancellationToken ct = default);

    Task<AppResult<UnreadCountResponse>> UnreadCountAsync(Guid userId, CancellationToken ct = default);

    Task<AppResult> MarkReadAsync(Guid userId, Guid notificationId, CancellationToken ct = default);

    Task<AppResult> MarkAllReadAsync(Guid userId, CancellationToken ct = default);
}
