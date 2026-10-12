using Channeled.Domain.Files;

namespace Channeled.Application.Files;

public sealed record CreateFolderRequest(string Name, Guid? ParentFolderId);

public sealed record RenameItemRequest(string Name);

public sealed record MoveItemRequest(Guid? TargetFolderId);

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
    bool Starred = false);

public sealed record ItemListResponse(
    IReadOnlyList<FileItemDto> Items,
    string? NextCursor);

public sealed record ListItemsQuery(
    Guid? FolderId,
    string? Cursor,
    int Limit,
    string? Sort,
    string? Type,
    Guid? UploaderId,
    bool TrashOnly);

public sealed record DownloadResult(
    Stream Content,
    string FileName,
    string ContentType,
    long SizeBytes,
    long RangeStart,
    long RangeEndInclusive,
    bool IsPartial);
