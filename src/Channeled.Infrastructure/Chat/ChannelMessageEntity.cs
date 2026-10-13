namespace Channeled.Infrastructure.Chat;

/// <summary>
/// A text-channel message. Ids are UUIDv7. Soft-delete scrubs <see cref="Body"/> to the empty string in the
/// same UPDATE (enforced by a DB check); the purge job later removes rows past retention.
/// </summary>
public sealed class ChannelMessageEntity
{
    public Guid Id { get; set; }
    public Guid ChannelId { get; set; }
    public Guid AuthorId { get; set; }
    public string Body { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
}
