namespace Channeled.Application.PublicLinks;

public sealed record CreateShareLinkRequest(int? ExpiresInDays, int? MaxDownloads);

/// <summary>Returned exactly once at creation; <see cref="Token"/> is never retrievable again.</summary>
public sealed record ShareLinkCreatedDto(
    Guid Id,
    string Token,
    string Path,
    Guid? FileId,
    string FileName,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    int? MaxDownloads);

/// <summary>Metadata only. Never contains the token or its hash.</summary>
public sealed record ShareLinkDto(
    Guid Id,
    Guid? FileId,
    Guid ChannelId,
    Guid OriginalChannelId,
    Guid TeamId,
    string FileName,
    Guid CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    int? MaxDownloads,
    int DownloadCount,
    DateTimeOffset? RevokedAt,
    string? RevokeReason,
    string Status);

public sealed record AdminRevokeAllShareLinksResult(int Revoked);

public sealed record PublicShareInfoDto(
    string FileName,
    long SizeBytes,
    string ContentType,
    DateTimeOffset ExpiresAt,
    int? RemainingDownloads);

public sealed record ClaimShareRequest(string? IdempotencyKey);

public sealed record ShareClaimDto(
    string Ticket,
    DateTimeOffset TicketExpiresAt,
    string DownloadPath,
    string FileName,
    long SizeBytes);

/// <summary>An opened anonymous download. Disposing releases the stream lease.</summary>
public sealed record PublicDownloadResult(
    Stream Content,
    string FileName,
    string ContentType,
    long SizeBytes,
    long RangeStart,
    long RangeEndInclusive,
    bool IsPartial);
