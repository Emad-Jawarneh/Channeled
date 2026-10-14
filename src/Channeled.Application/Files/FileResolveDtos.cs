namespace Channeled.Application.Files;

public sealed record ResolveFilesRequest(IReadOnlyList<Guid> FileIds);

public sealed record ResolvedFileDto(
    Guid FileId,
    bool Accessible,
    string? Name,
    long? SizeBytes,
    string? ContentType,
    string? ChannelName,
    Guid? FolderId,
    string State);

public sealed record ResolveFilesResponse(IReadOnlyList<ResolvedFileDto> Items);
