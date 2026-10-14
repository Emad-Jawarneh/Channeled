namespace Channeled.Application.Identity;

public enum SignInStatus
{
    SignedIn = 1,
    Failed = 2,
    LockedOut = 3,
}

/// <summary>Result of credential check; session creation happens separately so a future second step can sit in front.</summary>
public sealed record SignInOutcome(SignInStatus Status, AppUserRef? User, string? ErrorCode, string? Message);

public sealed record AppUserRef(
    Guid Id,
    string Email,
    string DisplayName,
    string Username,
    long? AvatarVersion,
    bool IsPlatformAdmin,
    string? Bio = null);
