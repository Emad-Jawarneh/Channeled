using Channeled.Application.Abstractions.Results;

namespace Channeled.Application.Files;

/// <summary>
/// A file the caller may download right now (ACL and liveness already checked). Metadata only: the API layer
/// evaluates Range/If-Range against it and opens the store stream itself.
/// </summary>
public sealed record DownloadTarget(
    Guid FileId,
    string StorageKey,
    string FileName,
    string ContentType,
    long SizeBytes,
    string Sha256,
    DateTimeOffset CreatedAt,
    Guid? ChannelId,
    Guid? OwnerUserId);

/// <summary><c>Downloads:*</c> — signed-in download limits and ticket windows.</summary>
public sealed class DownloadOptions
{
    public const string SectionName = "Downloads";

    /// <summary>Concurrent attachment downloads per user (inline previews are not counted).</summary>
    public int MaxConcurrentPerUser { get; set; } = 4;

    /// <summary>Concurrent attachment downloads across all signed-in users.</summary>
    public int MaxConcurrentGlobal { get; set; } = 64;

    /// <summary>Per-stream egress cap in bytes/second; 0 = unlimited.</summary>
    public long PerStreamBytesPerSecond { get; set; }

    /// <summary>A fresh (non-resume) request must start within this many seconds of issue.</summary>
    public int TicketStartSeconds { get; set; } = 60;

    /// <summary>Browser resume (Range + matching If-Range) is accepted for this long after issue.</summary>
    public int TicketResumeHours { get; set; } = 24;

    public int MaxTicketsPerRequest { get; set; } = 50;
}

/// <summary>One file to download. <c>Scope</c> is <c>channel</c> (needs team slug + channel name) or <c>personal</c>.</summary>
public sealed record DownloadTicketItem(string Scope, Guid ItemId, string? TeamSlug = null, string? ChannelName = null);

public sealed record DownloadTicketsRequest(IReadOnlyList<DownloadTicketItem>? Items);

/// <summary>Either <c>Url</c> (start a native download) or an error <c>Code</c> + <c>Message</c> for that item.</summary>
public sealed record DownloadTicketResult(Guid ItemId, string? Url, string? Code, string? Message);

public sealed record DownloadTicketsResponse(IReadOnlyList<DownloadTicketResult> Results, int MaxConcurrentDownloads);

/// <summary>A validated ticket: the current target plus when the ticket was issued.</summary>
public sealed record TicketedDownload(DownloadTarget Target, DateTimeOffset IssuedAt);

public interface IDownloadTicketService
{
    Task<AppResult<DownloadTicketsResponse>> IssueAsync(
        Guid userId,
        Guid sessionId,
        DownloadTicketsRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Verifies the ticket belongs to this user and session and re-checks ACL and liveness. The caller applies
    /// the fresh-start window, because whether a request is a resume depends on If-Range vs the target's ETag.
    /// </summary>
    Task<AppResult<TicketedDownload>> ResolveAsync(Guid userId, Guid sessionId, string ticket, CancellationToken ct = default);
}

/// <summary>Concurrent signed-in attachment download slots (per user and global).</summary>
public interface IDownloadSlots
{
    int ActiveFor(Guid userId);

    int ActiveGlobal { get; }

    /// <summary>Returns a lease to dispose when the stream ends (completed or aborted), or null when full.</summary>
    IDisposable? TryAcquire(Guid userId);
}
