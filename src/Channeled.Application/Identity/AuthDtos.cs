namespace Channeled.Application.Identity;

public sealed record LoginRequest(string Email, string Password);

public sealed record AuthUserDto(
    Guid Id,
    string Email,
    string DisplayName,
    string Username,
    long? AvatarVersion,
    bool IsPlatformAdmin,
    bool HasChangedUsername = false,
    DateTimeOffset? UsernameChangedAt = null,
    string? Bio = null);

public sealed record SessionDto(
    Guid Id,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt,
    string Ip,
    string UserAgent,
    bool IsCurrent);

public sealed record CreateInvitationRequest(string Email, Guid? InvitedByUserId);

public sealed record InvitationCreatedDto(Guid InvitationId, string RawToken, DateTimeOffset ExpiresAt);

public sealed record AcceptInvitationRequest(string Token, string DisplayName, string Password);

public sealed record RegisterRequest(string Email, string DisplayName, string Password);

public sealed record ForgotPasswordRequest(string Email);

public sealed record ResetPasswordRequest(string Token, string NewPassword);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record SeedOperatorRequest(string Email, string DisplayName, string Password);

public sealed record RequestContext(string Ip, string UserAgent, Guid? CurrentSessionId = null);
