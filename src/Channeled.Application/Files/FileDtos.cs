using Channeled.Domain.Files;

namespace Channeled.Application.Files;

public sealed record CreateFolderRequest(string Name, Guid? ParentFolderId);

public sealed record RenameItemRequest(string Name);

/// <summary>
/// Moves an item. When <paramref name="TargetChannelName"/> is set, a file is moved to another channel
/// of the same team (<paramref name="TargetFolderId"/> then refers to a folder in that channel).
/// </summary>
public sealed record MoveItemRequest(Guid? TargetFolderId, string? TargetChannelName = null);

public sealed record StartUploadRequest(
    string TeamSlug,
    string ChannelName,
    string FileName,
    long DeclaredSizeBytes,
    string? ContentType,
    Guid? FolderId);

public sealed record StartUploadResponse(
    Guid UploadId,
    string MultipartId,
    int PartSizeBytes,
    DateTimeOffset ExpiresAt);

public sealed record UploadPartResponse(int PartNumber, string ETag, long SizeBytes);

public sealed record UploadPartInfo(int PartNumber, string ETag, long SizeBytes);

public sealed record CompleteUploadRequest(string? ExpectedSha256Hex, string? IdempotencyKey);

public sealed record FileItemDto(
    Guid Id,
    ItemKind Kind,
    string Name,
    Guid? FolderId,
    Guid? ChannelId,
    string? ContentType,
    long? SizeBytes,
    string? Sha256,
    Guid? UploadedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? DeletedAt,
    Guid? OwnerUserId = null,
    bool Starred = false,
    string? UploadedByDisplayName = null,
    string? UploadedByUsername = null,
    long? UploadedByAvatarVersion = null,
    /// <summary><c>ok</c> when the uploader exists; <c>deleted</c> when the user row is gone.</summary>
    string? UploadedByStatus = null);

public sealed record FolderPathSegmentDto(Guid Id, string Name);

public sealed record ItemListResponse(
    IReadOnlyList<FileItemDto> Items,
    string? NextCursor,
    IReadOnlyList<FolderPathSegmentDto>? Path = null);

/// <param name="Sort">name | size | created | updated | type, optionally _asc/_desc (default updated_desc).</param>
/// <param name="NameContains">Case-insensitive literal name filter (folders and files).</param>
public sealed record ListItemsQuery(
    Guid? FolderId,
    string? Cursor,
    int Limit,
    string? Sort,
    string? Type,
    Guid? UploaderId,
    bool TrashOnly,
    string? NameContains = null);
