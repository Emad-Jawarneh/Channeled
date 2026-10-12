using Channeled.Application.Files;

namespace Channeled.Application.Personal;

public sealed record PersonalStorageDto(long QuotaBytes, long UsageBytes);

public sealed record StartPersonalUploadRequest(
    string FileName,
    long DeclaredSizeBytes,
    string? ContentType,
    Guid? FolderId);

public sealed record CopyToChannelRequest(
    string TeamSlug,
    string ChannelName,
    Guid? TargetFolderId,
    string? NewName);
