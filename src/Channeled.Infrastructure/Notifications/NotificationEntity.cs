using Channeled.Domain.Notifications;

namespace Channeled.Infrastructure.Notifications;

public sealed class NotificationEntity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public NotificationType Type { get; set; }
    public Guid TeamId { get; set; }
    public Guid ChannelId { get; set; }
    public Guid MessageId { get; set; }
    public Guid ActorId { get; set; }
    public string BodyPreview { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
}
