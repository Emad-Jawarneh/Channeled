namespace Channeled.Application.Identity;

/// <summary>Shared person chip shape used across members, chat, notifications, and account.</summary>
public sealed record UserSummary(
    Guid Id,
    string DisplayName,
    string Username,
    long? AvatarVersion);

public static class UserSummaryMapper
{
    public static UserSummary From(
        Guid id,
        string displayName,
        string username,
        DateTimeOffset? avatarUpdatedAt) =>
        new(
            id,
            displayName,
            username,
            avatarUpdatedAt?.ToUnixTimeMilliseconds());
}
