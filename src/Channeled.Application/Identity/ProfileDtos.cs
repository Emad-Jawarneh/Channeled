namespace Channeled.Application.Identity;

public sealed record UserCardDto(
    Guid Id,
    string DisplayName,
    string Username,
    long? AvatarVersion,
    string? Email,
    string? Bio = null);

public sealed record UpdateProfileRequest(string? DisplayName, string? Username, string? Bio = null);

public sealed record UsernameAvailabilityDto(bool Available, string? Reason);

public sealed record StartEmailChangeRequest(string NewEmail, string CurrentPassword);

public sealed record ConfirmEmailChangeRequest(string Token);

public sealed record CancelEmailChangeRequest(string Token);

public sealed record CancelTokenPasswordChangeRequest(string Token, string NewPassword);

public sealed record AdminCorrectEmailRequest(string Email, string AdminPassword);
