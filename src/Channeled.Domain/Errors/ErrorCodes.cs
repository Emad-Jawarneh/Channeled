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
}
