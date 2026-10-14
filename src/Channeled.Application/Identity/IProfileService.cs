using Channeled.Application.Abstractions.Results;

namespace Channeled.Application.Identity;

public sealed record AvatarOpenResult(Stream Content, string ContentType, DateTimeOffset? UpdatedAt);

public interface IProfileService
{
    Task<AppResult<AuthUserDto>> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, RequestContext context, CancellationToken ct = default);

    Task<AppResult<UsernameAvailabilityDto>> CheckUsernameAsync(Guid userId, string username, CancellationToken ct = default);

    Task<AppResult<UserCardDto>> GetCardAsync(Guid viewerId, Guid targetUserId, CancellationToken ct = default);

    Task<AppResult<AvatarOpenResult>> OpenAvatarAsync(Guid viewerId, Guid targetUserId, CancellationToken ct = default);

    Task<AppResult<AuthUserDto>> UploadAvatarAsync(
        Guid userId,
        Stream content,
        long? contentLength,
        RequestContext context,
        CancellationToken ct = default);

    Task<AppResult> DeleteAvatarAsync(Guid userId, RequestContext context, CancellationToken ct = default);
}
