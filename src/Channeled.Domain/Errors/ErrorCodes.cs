namespace Channeled.Domain.Errors;

public static class ErrorCodes
{
    public const string Validation = "validation_error";
    public const string Unauthorized = "unauthorized";
    public const string Forbidden = "forbidden";
    public const string NotFound = "not_found";
    public const string Conflict = "conflict";
    public const string RateLimited = "rate_limited";
    public const string LockedOut = "locked_out";
    public const string InvalidToken = "invalid_token";
    public const string CsrfRejected = "csrf_rejected";
    public const string NameConflict = "name_conflict";
    public const string TeamQuotaExceeded = "team_quota_exceeded";
    public const string PersonalQuotaExceeded = "personal_quota_exceeded";
    public const string FileTooLarge = "file_too_large";
    public const string FolderDepthExceeded = "folder_depth_exceeded";
    public const string UploadExpired = "upload_expired";
    public const string UploadInvalidState = "upload_invalid_state";
    public const string ChecksumMismatch = "checksum_mismatch";
    public const string ShareLinkLimit = "share_link_limit";
    public const string ShareTicketInvalid = "share_ticket_invalid";
    public const string RangeNotSatisfiable = "range_not_satisfiable";
    public const string MessageTooLong = "message_too_long";
    public const string WrongChannelKind = "wrong_channel_kind";
    public const string ChannelArchived = "channel_archived";
    public const string LockTimeout = "lock_timeout";
    public const string Gone = "gone";
    /// <summary>Unknown, rotated, or expired team join link (always the same generic message).</summary>
    public const string JoinLinkInvalid = "join_link_invalid";
    /// <summary>Stored join token can no longer be decrypted; rotate the link.</summary>
    public const string JoinLinkUnavailable = "join_link_unavailable";
    public const string JoinCooldown = "join_cooldown";
    public const string JoinRateLimited = "join_rate_limited";
    public const string OpenJoinConfirmationRequired = "open_join_confirmation_required";
    public const string OpenJoinTooManyPending = "open_join_too_many_pending";
    public const string OwnerMustTransfer = "owner_must_transfer";
    /// <summary>A download ticket was used for a fresh (non-resume) request after its start window.</summary>
    public const string DownloadTicketExpired = "download_ticket_expired";
    public const string TooManyDownloads = "too_many_downloads";
}
