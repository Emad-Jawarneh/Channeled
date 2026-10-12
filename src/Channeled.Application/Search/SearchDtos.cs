using Channeled.Domain.Files;
using Channeled.Domain.Search;

namespace Channeled.Application.Search;

public sealed record SearchQuery(
    string Q,
    SearchScope Scope,
    string? TeamSlug,
    string? ChannelName,
    string? Type,
    Guid? UploaderId,
    string? Cursor,
    int Limit);

public sealed record SearchHitDto(
    Guid Id,
    ItemKind Kind,
    string Name,
    Guid? ChannelId,
    Guid? OwnerUserId,
    string? ContentType,
    long? SizeBytes,
    Guid? UploadedBy,
    DateTimeOffset UpdatedAt,
    string? ChannelName,
    string? TeamSlug);

public sealed record SearchResponse(
    IReadOnlyList<SearchHitDto> Hits,
    string? NextCursor);

public sealed record TeamStorageDto(long QuotaBytes, long UsageBytes);

public sealed record QuotaReconciliationResult(
    int TeamsUpdated,
    int UsersUpdated,
    DateTimeOffset CompletedAt);
