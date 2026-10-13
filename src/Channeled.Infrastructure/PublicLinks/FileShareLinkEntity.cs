namespace Channeled.Infrastructure.PublicLinks;

/// <summary>
/// A public share link. Intentionally has NO EF navigation/relationship to <c>FileEntity</c>:
/// the composite FK (file_id, channel_id) -> files (id, channel_id) with
/// ON UPDATE CASCADE ON DELETE SET NULL (file_id) is created via raw SQL in the migration
/// because EF cannot express a column-list SET NULL for composite keys.
/// </summary>
public sealed class FileShareLinkEntity
{
    public Guid Id { get; set; }

    /// <summary>SHA-256 hex of the raw token. The raw token is never stored.</summary>
    public string TokenHash { get; set; } = "";

    /// <summary>Nullable: set to NULL by the database when the file row is purged.</summary>
    public Guid? FileId { get; set; }

    /// <summary>Current channel of the file (cascades on cross-channel move; link is revoked then).</summary>
    public Guid ChannelId { get; set; }

    /// <summary>Channel at creation time. No FK; survives channel/file deletion.</summary>
    public Guid OriginalChannelId { get; set; }

    public Guid TeamId { get; set; }

    /// <summary>File name snapshot at creation time.</summary>
    public string FileName { get; set; } = "";

    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Blocks NEW claims only; already-issued tickets outlive it.</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    public int? MaxDownloads { get; set; }
    public int DownloadCount { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? RevokedBy { get; set; }
    public string? RevokeReason { get; set; }
}
