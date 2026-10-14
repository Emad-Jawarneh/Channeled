namespace Channeled.Application.Chat;

public sealed record PostMessageRequest(
    string? Body,
    Guid? ReplyToMessageId = null,
    IReadOnlyList<Guid>? MentionedUserIds = null,
    IReadOnlyList<Guid>? FileIds = null);

public sealed record MessageReplyPreviewDto(
    Guid Id,
    Guid AuthorId,
    string AuthorDisplayName,
    string BodyPreview,
    bool Deleted);

public sealed record MessageMentionDto(Guid UserId, string DisplayName);

public sealed record ChannelMessageDto(
    Guid Id,
    Guid ChannelId,
    Guid AuthorId,
    string AuthorDisplayName,
    string Body,
    DateTimeOffset CreatedAt,
    MessageReplyPreviewDto? ReplyTo,
    IReadOnlyList<MessageMentionDto> Mentions,
    IReadOnlyList<Guid> FileIds);

/// <summary>Newest-first page. <c>NextCursor</c> pages toward older messages.</summary>
public sealed record MessageListResponse(IReadOnlyList<ChannelMessageDto> Items, string? NextCursor);

public sealed record ListMessagesQuery(string? Cursor, int Limit);

public sealed record PresenceUserDto(Guid UserId, string DisplayName);

public sealed record PresenceListResponse(Guid ChannelId, IReadOnlyList<PresenceUserDto> Users);

/// <summary>Payload of the hub <c>messageDeleted</c> event.</summary>
public sealed record MessageDeletedEvent(Guid ChannelId, Guid MessageId);

/// <summary>Payload of the hub <c>presenceChanged</c> event.</summary>
public sealed record PresenceChangedEvent(Guid ChannelId, Guid UserId, string DisplayName, bool Online);
