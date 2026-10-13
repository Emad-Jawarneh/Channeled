namespace Channeled.Application.Chat;

public sealed record PostMessageRequest(string? Body);

public sealed record ChannelMessageDto(
    Guid Id,
    Guid ChannelId,
    Guid AuthorId,
    string AuthorDisplayName,
    string Body,
    DateTimeOffset CreatedAt);

/// <summary>Newest-first page. <c>NextCursor</c> pages toward older messages.</summary>
public sealed record MessageListResponse(IReadOnlyList<ChannelMessageDto> Items, string? NextCursor);

public sealed record ListMessagesQuery(string? Cursor, int Limit);

public sealed record PresenceUserDto(Guid UserId, string DisplayName);

public sealed record PresenceListResponse(Guid ChannelId, IReadOnlyList<PresenceUserDto> Users);

/// <summary>Payload of the hub <c>messageDeleted</c> event.</summary>
public sealed record MessageDeletedEvent(Guid ChannelId, Guid MessageId);

/// <summary>Payload of the hub <c>presenceChanged</c> event.</summary>
public sealed record PresenceChangedEvent(Guid ChannelId, Guid UserId, string DisplayName, bool Online);
