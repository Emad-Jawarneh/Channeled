using Channeled.Application.Identity;

namespace Channeled.Infrastructure.Identity;

public static class AuthMapping
{
    public static AuthUserDto ToDto(AppUser user) =>
        new(
            user.Id,
            user.Email ?? "",
            user.DisplayName,
            user.UsernameSlug,
            user.AvatarUpdatedAt?.ToUnixTimeMilliseconds(),
            user.IsPlatformAdmin,
            user.HasChangedUsername,
            user.UsernameChangedAt,
            string.IsNullOrEmpty(user.Bio) ? null : user.Bio);

    public static UserSummary ToSummary(AppUser user) =>
        UserSummaryMapper.From(
            user.Id,
            user.DisplayName,
            user.UsernameSlug,
            user.AvatarUpdatedAt);
}
