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
}
