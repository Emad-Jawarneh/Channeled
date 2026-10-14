namespace Channeled.Infrastructure.Chat;

/// <summary>Structured attachment of a team file to a text-channel message (ids only over the wire).</summary>
public sealed class MessageFileRefEntity
{
    public Guid MessageId { get; set; }
    public Guid FileId { get; set; }

    public ChannelMessageEntity? Message { get; set; }
}
