using Channeled.Domain.Notifications;

namespace Channeled.Application.Notifications;

public sealed record NotificationDto(
    Guid Id,
    NotificationType Type,
    Guid TeamId,
    string TeamSlug,
    Guid ChannelId,
    string ChannelName,
    Guid MessageId,
    Guid ActorId,
    string ActorDisplayName,
    string BodyPreview,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt);

public sealed record NotificationListResponse(IReadOnlyList<NotificationDto> Items, int UnreadCount);

public sealed record UnreadCountResponse(int UnreadCount);
