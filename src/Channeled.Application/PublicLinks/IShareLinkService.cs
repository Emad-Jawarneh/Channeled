using Channeled.Application.Abstractions.Results;
using Channeled.Application.Identity;

namespace Channeled.Application.PublicLinks;

public interface IShareLinkService
{
    Task<AppResult<ShareLinkCreatedDto>> CreateAsync(
        Guid userId,
        string teamSlug,
        string channelName,
        Guid fileId,
        CreateShareLinkRequest request,
        RequestContext context,
        CancellationToken ct = default);

    /// <summary>Lists link metadata in a channel. Creators see their own; Admin+ see all.</summary>
    Task<AppResult<IReadOnlyList<ShareLinkDto>>> ListAsync(
        Guid userId,
        string teamSlug,
        string channelName,
        Guid? fileId,
        CancellationToken ct = default);

    Task<AppResult> RevokeAsync(
        Guid userId,
        string teamSlug,
        Guid linkId,
        RequestContext context,
        CancellationToken ct = default);

    Task<AppResult<IReadOnlyList<ShareLinkDto>>> AdminListAsync(
        Guid actorId,
        bool activeOnly,
        CancellationToken ct = default);

    Task<AppResult<AdminRevokeAllShareLinksResult>> AdminRevokeAllAsync(
        Guid actorId,
        RequestContext context,
        CancellationToken ct = default);

    Task<AppResult<PublicShareInfoDto>> GetPublicInfoAsync(string token, CancellationToken ct = default);

    /// <summary>
    /// Consumes one download from the link and issues an HMAC ticket. Idempotent per
    /// (token hash, idempotency key) within the configured window.
    /// </summary>
    Task<AppResult<ShareClaimDto>> ClaimAsync(
        string token,
        string? idempotencyKey,
        RequestContext context,
        CancellationToken ct = default);

    /// <summary>Opens a guarded (throttled, periodically re-validated, byte-capped) anonymous stream.</summary>
    Task<AppResult<PublicDownloadResult>> OpenTicketStreamAsync(
        string token,
        string ticket,
        long? rangeStart,
        long? rangeEndInclusive,
        string clientIp,
        CancellationToken ct = default);
}
