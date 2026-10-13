using System.Text.Json;
using Channeled.Application.Abstractions.Results;
using Channeled.Application.Abstractions.Storage;
using Channeled.Application.Abstractions.Time;
using Channeled.Application.Identity;
using Channeled.Application.PublicLinks;
using Channeled.Domain.Audit;
using Channeled.Domain.Authorization;
using Channeled.Domain.Errors;
using Channeled.Domain.Identity;
using Channeled.Domain.Teams;
using Channeled.Infrastructure.Files;
using Channeled.Infrastructure.Persistence;
using Channeled.Infrastructure.Security;
using Channeled.Infrastructure.Teams;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Channeled.Infrastructure.PublicLinks;

public sealed class ShareLinkService(
    ApplicationDbContext db,
    IAuthorizer authorizer,
    IFileStore store,
    IClock clock,
    IOptionsMonitor<PublicFileLinksOptions> optionsMonitor,
    HmacTicketSigner signer,
    ClaimIdempotencyStore claimStore,
    IAnonDownloadLimiter limiter,
    TicketUsageTracker usage,
    IServiceScopeFactory scopeFactory) : IShareLinkService
{
    private const int MaxListed = 200;
    private const int MaxDownloadsCeiling = 10_000;

    // ------------------------------------------------------------------ authenticated

    public async Task<AppResult<ShareLinkCreatedDto>> CreateAsync(
        Guid userId,
        string teamSlug,
        string channelName,
        Guid fileId,
        CreateShareLinkRequest request,
        RequestContext context,
        CancellationToken ct = default)
    {
        var opts = optionsMonitor.CurrentValue;
        if (!opts.IsOperational)
        {
            return AppResult<ShareLinkCreatedDto>.Fail(ErrorCodes.NotFound, "Public file links are disabled.");
        }

        var access = await LoadChannelAsync(userId, teamSlug, channelName, ct);
        if (access is null || !Allow(userId, AuthAction.DownloadFile, access))
        {
            return AppResult<ShareLinkCreatedDto>.Fail(ErrorCodes.NotFound, "Channel not found.");
        }

        if (access.Channel.Kind == ChannelKind.Text)
        {
            return AppResult<ShareLinkCreatedDto>.Fail(ErrorCodes.Validation, "Text channels do not store files.");
        }

        if (access.Channel.ArchivedAt is not null || access.Team.ArchivedAt is not null)
        {
            return AppResult<ShareLinkCreatedDto>.Fail(ErrorCodes.Validation, "Archived channels cannot be shared.");
        }

        var file = await db.Files.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == fileId && f.ChannelId == access.Channel.Id && f.DeletedAt == null, ct);
        if (file is null || !await IsFolderChainLiveAsync(file.FolderId, ct))
        {
            return AppResult<ShareLinkCreatedDto>.Fail(ErrorCodes.NotFound, "File not found.");
        }

        if (file.SizeBytes > opts.MaxFileBytes)
        {
            return AppResult<ShareLinkCreatedDto>.Fail(ErrorCodes.FileTooLarge, "File is too large to share publicly.");
        }

        var days = request.ExpiresInDays ?? opts.DefaultExpiryDays;
        if (days < 1 || days > opts.MaxExpiryDays)
        {
            return AppResult<ShareLinkCreatedDto>.Fail(
                ErrorCodes.Validation, $"expiresInDays must be between 1 and {opts.MaxExpiryDays}.");
        }

        var maxDownloads = request.MaxDownloads ?? opts.DefaultMaxDownloads;
        if (maxDownloads < 1 || maxDownloads > MaxDownloadsCeiling)
        {
            return AppResult<ShareLinkCreatedDto>.Fail(
                ErrorCodes.Validation, $"maxDownloads must be between 1 and {MaxDownloadsCeiling}.");
        }

        var now = clock.UtcNow;
        var active = await db.FileShareLinks.CountAsync(
            l => l.TeamId == access.Team.Id && l.RevokedAt == null && l.ExpiresAt > now, ct);
        if (active >= opts.MaxActiveLinksPerTeam)
        {
            return AppResult<ShareLinkCreatedDto>.Fail(
                ErrorCodes.ShareLinkLimit, "This team has reached its maximum number of active public links.");
        }

        var rawToken = TokenHasher.CreateRawToken();
        var link = new FileShareLinkEntity
        {
            Id = UuidV7.NewGuid(),
            TokenHash = TokenHasher.Hash(rawToken),
            FileId = file.Id,
            ChannelId = access.Channel.Id,
            OriginalChannelId = access.Channel.Id,
            TeamId = access.Team.Id,
            FileName = file.Name,
            CreatedBy = userId,
            CreatedAt = now,
            ExpiresAt = now.AddDays(days),
            MaxDownloads = maxDownloads,
            DownloadCount = 0,
        };
        db.FileShareLinks.Add(link);
        WriteAudit(
            userId,
            AuditEventType.FileShareCreate,
            link,
            context,
            JsonSerializer.Serialize(new { fileId = file.Id, expiresAt = link.ExpiresAt, maxDownloads }));
        await db.SaveChangesAsync(ct);

        return AppResult<ShareLinkCreatedDto>.Ok(new ShareLinkCreatedDto(
            link.Id,
            rawToken,
            "/s/" + rawToken,
            link.FileId,
            link.FileName,
            link.CreatedAt,
            link.ExpiresAt,
            link.MaxDownloads));
    }

    public async Task<AppResult<IReadOnlyList<ShareLinkDto>>> ListAsync(
        Guid userId,
        string teamSlug,
        string channelName,
        Guid? fileId,
        CancellationToken ct = default)
    {
        var access = await LoadChannelAsync(userId, teamSlug, channelName, ct);
        if (access is null || !Allow(userId, AuthAction.BrowseFiles, access))
        {
            return AppResult<IReadOnlyList<ShareLinkDto>>.Fail(ErrorCodes.NotFound, "Channel not found.");
        }

        var channelId = access.Channel.Id;
        var query = db.FileShareLinks.AsNoTracking().Where(l => l.OriginalChannelId == channelId);
        if (access.Role < MemberRole.Admin)
        {
            query = query.Where(l => l.CreatedBy == userId);
        }

        if (fileId is Guid fid)
        {
            query = query.Where(l => l.FileId == fid);
        }

        var rows = await query.OrderByDescending(l => l.CreatedAt).Take(MaxListed).ToListAsync(ct);
        var now = clock.UtcNow;
        IReadOnlyList<ShareLinkDto> list = rows.Select(l => ToDto(l, now)).ToList();
        return AppResult<IReadOnlyList<ShareLinkDto>>.Ok(list);
    }

    public async Task<AppResult> RevokeAsync(
        Guid userId,
        string teamSlug,
        Guid linkId,
        RequestContext context,
        CancellationToken ct = default)
    {
        var team = await db.Teams.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == teamSlug, ct);
        var member = team is null
            ? null
            : await db.TeamMembers.AsNoTracking().FirstOrDefaultAsync(m => m.TeamId == team.Id && m.UserId == userId, ct);
        var link = team is null ? null : await db.FileShareLinks.FirstOrDefaultAsync(l => l.Id == linkId && l.TeamId == team.Id, ct);
        if (member is null || link is null || !(link.CreatedBy == userId || member.Role >= MemberRole.Admin))
        {
            return AppResult.Fail(ErrorCodes.NotFound, "Share link not found.");
        }

        if (link.RevokedAt is null)
        {
            ShareLinkRevoker.Apply(db, clock, link, ShareLinkRevoker.ReasonManual, userId, context);
            await db.SaveChangesAsync(ct);
        }

        return AppResult.Ok();
    }

    public async Task<AppResult<IReadOnlyList<ShareLinkDto>>> AdminListAsync(
        Guid actorId,
        bool activeOnly,
        CancellationToken ct = default)
    {
        if (!await IsPlatformAdminAsync(actorId, ct))
        {
            return AppResult<IReadOnlyList<ShareLinkDto>>.Fail(ErrorCodes.NotFound, "Not found.");
        }

        var now = clock.UtcNow;
        var query = db.FileShareLinks.AsNoTracking().AsQueryable();
        if (activeOnly)
        {
            query = query.Where(l => l.RevokedAt == null);
        }

        var rows = await query.OrderByDescending(l => l.CreatedAt).Take(1000).ToListAsync(ct);
        IReadOnlyList<ShareLinkDto> list = rows.Select(l => ToDto(l, now)).ToList();
        return AppResult<IReadOnlyList<ShareLinkDto>>.Ok(list);
    }

    public async Task<AppResult<AdminRevokeAllShareLinksResult>> AdminRevokeAllAsync(
        Guid actorId,
        RequestContext context,
        CancellationToken ct = default)
    {
        if (!await IsPlatformAdminAsync(actorId, ct))
        {
            return AppResult<AdminRevokeAllShareLinksResult>.Fail(ErrorCodes.NotFound, "Not found.");
        }

        var count = await ShareLinkRevoker.RevokeAsync(
            db, clock, _ => true, ShareLinkRevoker.ReasonAdmin, actorId, context, ct);
        await db.SaveChangesAsync(ct);
        return AppResult<AdminRevokeAllShareLinksResult>.Ok(new AdminRevokeAllShareLinksResult(count));
    }

    // ------------------------------------------------------------------ anonymous

    public async Task<AppResult<PublicShareInfoDto>> GetPublicInfoAsync(string token, CancellationToken ct = default)
    {
        var (link, error) = await FindClaimableLinkAsync(token, ct);
        if (link is null)
        {
            return AppResult<PublicShareInfoDto>.Fail(ErrorCodes.NotFound, error ?? "Link not found.");
        }

        var file = await ResolveLiveFileAsync(link, ct);
        if (file is null)
        {
            return AppResult<PublicShareInfoDto>.Fail(ErrorCodes.NotFound, "Link not found.");
        }

        int? remaining = link.MaxDownloads is int max ? Math.Max(0, max - link.DownloadCount) : null;
        return AppResult<PublicShareInfoDto>.Ok(new PublicShareInfoDto(
            link.FileName, file.SizeBytes, file.ContentType, link.ExpiresAt, remaining));
    }

    public async Task<AppResult<ShareClaimDto>> ClaimAsync(
        string token,
        string? idempotencyKey,
        RequestContext context,
        CancellationToken ct = default)
    {
        if (!optionsMonitor.CurrentValue.IsOperational || !IsPlausibleToken(token))
        {
            return AppResult<ShareClaimDto>.Fail(ErrorCodes.NotFound, "Link not found.");
        }

        var key = string.IsNullOrWhiteSpace(idempotencyKey)
            ? Guid.NewGuid().ToString("N") // no key => never deduplicated
            : idempotencyKey.Trim();
        if (key.Length > 128)
        {
            return AppResult<ShareClaimDto>.Fail(ErrorCodes.Validation, "Idempotency key is too long.");
        }

        // The shared Lazy task must not depend on this request's scope or cancellation: run in its own scope.
        return await claimStore.GetOrAddAsync(TokenHasher.Hash(token), key, async () =>
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var svc = scope.ServiceProvider.GetRequiredService<ShareLinkService>();
            return await svc.ClaimCoreAsync(token, CancellationToken.None);
        });
    }

    /// <summary>Single non-idempotent claim: atomically consumes one download (no refund) and issues a ticket.</summary>
    public async Task<AppResult<ShareClaimDto>> ClaimCoreAsync(string token, CancellationToken ct)
    {
        var opts = optionsMonitor.CurrentValue;
        var (link, error) = await FindClaimableLinkAsync(token, ct);
        if (link is null)
        {
            return AppResult<ShareClaimDto>.Fail(ErrorCodes.NotFound, error ?? "Link not found.");
        }

        var file = await ResolveLiveFileAsync(link, ct);
        if (file is null)
        {
            return AppResult<ShareClaimDto>.Fail(ErrorCodes.NotFound, "Link not found.");
        }

        // Single atomic statement: concurrent claims cannot over-consume max_downloads. expires_at is
        // enforced here (blocks NEW claims only); tickets already issued are unaffected by link expiry.
        var now = clock.UtcNow;
        var linkId = link.Id;
        var rows = await db.FileShareLinks
            .Where(l => l.Id == linkId
                        && l.RevokedAt == null
                        && l.ExpiresAt > now
                        && (l.MaxDownloads == null || l.DownloadCount < l.MaxDownloads))
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.DownloadCount, l => l.DownloadCount + 1), ct);
        if (rows == 0)
        {
            return AppResult<ShareClaimDto>.Fail(ErrorCodes.NotFound, "This link is no longer available.");
        }

        var ticketExpires = now.AddHours(Math.Max(1, opts.TicketTtlHours));
        var ticket = signer.Sign(new ShareTicket(link.Id, Guid.NewGuid(), now, ticketExpires));
        return AppResult<ShareClaimDto>.Ok(new ShareClaimDto(
            ticket,
            ticketExpires,
            $"/api/v1/public/shares/{token}/download?ticket={Uri.EscapeDataString(ticket)}",
            link.FileName,
            file.SizeBytes));
    }

    public async Task<AppResult<PublicDownloadResult>> OpenTicketStreamAsync(
        string token,
        string ticket,
        long? rangeStart,
        long? rangeEndInclusive,
        string clientIp,
        CancellationToken ct = default)
    {
        var opts = optionsMonitor.CurrentValue;
        if (!opts.IsOperational || !IsPlausibleToken(token))
        {
            return AppResult<PublicDownloadResult>.Fail(ErrorCodes.NotFound, "Link not found.");
        }

        var now = clock.UtcNow;
        if (!signer.TryVerify(ticket, now, out var parsed) || parsed is null)
        {
            return AppResult<PublicDownloadResult>.Fail(ErrorCodes.ShareTicketInvalid, "Download ticket is invalid or expired.");
        }

        // Ticket outlives link ExpiresAt by design; it does NOT outlive revocation/trash/archive/purge.
        var tokenHash = TokenHasher.Hash(token);
        var link = await db.FileShareLinks.AsNoTracking()
            .FirstOrDefaultAsync(l => l.TokenHash == tokenHash && l.Id == parsed.LinkId, ct);
        if (link is null || link.RevokedAt is not null)
        {
            return AppResult<PublicDownloadResult>.Fail(ErrorCodes.NotFound, "Link not found.");
        }

        var file = await ResolveLiveFileAsync(link, ct);
        if (file is null)
        {
            return AppResult<PublicDownloadResult>.Fail(ErrorCodes.NotFound, "Link not found.");
        }

        var start = rangeStart ?? 0;
        var end = rangeEndInclusive ?? (file.SizeBytes - 1);
        if (file.SizeBytes == 0)
        {
            start = 0;
            end = -1;
        }
        else
        {
            if (start < 0 || start >= file.SizeBytes || end < start)
            {
                return AppResult<PublicDownloadResult>.Fail(ErrorCodes.RangeNotSatisfiable, "Invalid byte range.");
            }

            end = Math.Min(end, file.SizeBytes - 1);
        }

        var byteCap = checked(file.SizeBytes * Math.Max(1, opts.TicketByteCapMultiplier));
        var length = Math.Max(0, end - start + 1);
        if (usage.Get(parsed.TicketId) + length > byteCap)
        {
            return AppResult<PublicDownloadResult>.Fail(ErrorCodes.ShareTicketInvalid, "Download ticket byte cap reached.");
        }

        var lease = limiter.TryAcquire(clientIp);
        if (lease is null)
        {
            return AppResult<PublicDownloadResult>.Fail(ErrorCodes.RateLimited, "Too many anonymous downloads in progress. Try again shortly.");
        }

        Stream inner;
        try
        {
            inner = file.SizeBytes == 0
                ? new MemoryStream([])
                : await store.OpenReadAsync(file.StorageKey, start, end, ct);
        }
        catch
        {
            lease.Dispose();
            throw;
        }

        var linkId = link.Id;
        var guarded = new GuardedDownloadStream(
            inner,
            lease,
            parsed.TicketId,
            parsed.ExpiresAt,
            byteCap,
            usage,
            () => clock.UtcNow,
            rc => IsStreamStillValidAsync(linkId, rc),
            TimeSpan.FromSeconds(Math.Max(1, opts.MidStreamRecheckSeconds)));

        var partial = rangeStart is not null || rangeEndInclusive is not null;
        return AppResult<PublicDownloadResult>.Ok(new PublicDownloadResult(
            guarded,
            link.FileName,
            file.ContentType,
            file.SizeBytes,
            start,
            Math.Max(end, 0),
            partial));
    }

    /// <summary>Mid-stream recheck, run in its own scope (the stream outlives request-scoped queries).</summary>
    private async Task<bool> IsStreamStillValidAsync(Guid linkId, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<ShareLinkService>();
        return await svc.StillValidAsync(linkId, ct);
    }

    public async Task<bool> StillValidAsync(Guid linkId, CancellationToken ct)
    {
        if (!optionsMonitor.CurrentValue.IsOperational)
        {
            return false;
        }

        var link = await db.FileShareLinks.AsNoTracking().FirstOrDefaultAsync(l => l.Id == linkId, ct);
        if (link is null || link.RevokedAt is not null)
        {
            return false;
        }

        return await ResolveLiveFileAsync(link, ct) is not null;
    }

    // ------------------------------------------------------------------ helpers

    private async Task<(FileShareLinkEntity? Link, string? Error)> FindClaimableLinkAsync(string token, CancellationToken ct)
    {
        if (!optionsMonitor.CurrentValue.IsOperational || !IsPlausibleToken(token))
        {
            return (null, "Link not found.");
        }

        var hash = TokenHasher.Hash(token);
        var link = await db.FileShareLinks.AsNoTracking().FirstOrDefaultAsync(l => l.TokenHash == hash, ct);
        if (link is null || link.RevokedAt is not null)
        {
            return (null, "Link not found.");
        }

        var now = clock.UtcNow;
        if (link.ExpiresAt <= now)
        {
            return (null, "This link has expired.");
        }

        if (link.MaxDownloads is int max && link.DownloadCount >= max)
        {
            return (null, "This link has reached its download limit.");
        }

        return (link, null);
    }

    /// <summary>The file only if it still exists, is not trashed (itself or via a parent folder), and its channel/team are not archived.</summary>
    private async Task<FileEntity?> ResolveLiveFileAsync(FileShareLinkEntity link, CancellationToken ct)
    {
        if (link.FileId is not Guid fileId)
        {
            return null;
        }

        var file = await db.Files.AsNoTracking().FirstOrDefaultAsync(f => f.Id == fileId && f.DeletedAt == null, ct);
        if (file?.ChannelId is not Guid channelId)
        {
            return null;
        }

        var channelOk = await db.Channels.AsNoTracking()
            .AnyAsync(c => c.Id == channelId && c.ArchivedAt == null && c.Team!.ArchivedAt == null, ct);
        if (!channelOk || !await IsFolderChainLiveAsync(file.FolderId, ct))
        {
            return null;
        }

        return file;
    }

    private async Task<bool> IsFolderChainLiveAsync(Guid? folderId, CancellationToken ct)
    {
        var current = folderId;
        var guard = 0;
        while (current is not null)
        {
            if (++guard > 64)
            {
                return false;
            }

            var id = current;
            var folder = await db.Folders.AsNoTracking()
                .Where(f => f.Id == id)
                .Select(f => new { f.DeletedAt, f.ParentId })
                .FirstOrDefaultAsync(ct);
            if (folder is null || folder.DeletedAt is not null)
            {
                return false;
            }

            current = folder.ParentId;
        }

        return true;
    }

    private async Task<bool> IsPlatformAdminAsync(Guid actorId, CancellationToken ct) =>
        await db.Users.AsNoTracking().AnyAsync(u => u.Id == actorId && u.IsPlatformAdmin && u.Status == "active", ct);

    private static bool IsPlausibleToken(string? token) =>
        token is { Length: 64 } && token.All(Uri.IsHexDigit);

    private static ShareLinkDto ToDto(FileShareLinkEntity l, DateTimeOffset now)
    {
        var status = l.RevokedAt is not null ? "revoked"
            : l.ExpiresAt <= now ? "expired"
            : l.MaxDownloads is int max && l.DownloadCount >= max ? "exhausted"
            : "active";
        return new ShareLinkDto(
            l.Id, l.FileId, l.ChannelId, l.OriginalChannelId, l.TeamId, l.FileName, l.CreatedBy,
            l.CreatedAt, l.ExpiresAt, l.MaxDownloads, l.DownloadCount, l.RevokedAt, l.RevokeReason, status);
    }

    private void WriteAudit(
        Guid actorId,
        AuditEventType type,
        FileShareLinkEntity link,
        RequestContext context,
        string detailsJson)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            Id = UuidV7.NewGuid(),
            OccurredAt = clock.UtcNow,
            ActorId = actorId,
            EventType = type,
            TeamId = link.TeamId,
            TargetType = "file_share_link",
            TargetId = link.Id.ToString(),
            DetailsJson = detailsJson,
            Ip = Truncate(context.Ip, 64),
            UserAgent = Truncate(context.UserAgent, 512),
        });
    }

    private bool Allow(Guid userId, AuthAction action, ChannelAccess a) =>
        authorizer.Authorize(new AuthorizationContext
        {
            UserId = userId,
            Action = action,
            IsTeamMember = true,
            TeamRole = a.Role,
            TeamArchived = a.Team.ArchivedAt is not null,
            ChannelExists = true,
            ChannelVisibility = a.Channel.Visibility,
            ChannelArchived = a.Channel.ArchivedAt is not null,
            IsChannelMember = a.IsChannelMember,
            ChannelCanWrite = a.CanWrite,
            ChannelCanManage = a.CanManage,
            IsFileOwner = false,
        }).Allowed;

    private async Task<ChannelAccess?> LoadChannelAsync(Guid userId, string slug, string channelName, CancellationToken ct)
    {
        var team = await db.Teams.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == slug, ct);
        if (team is null)
        {
            return null;
        }

        var member = await db.TeamMembers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.TeamId == team.Id && m.UserId == userId, ct);
        if (member is null)
        {
            return null;
        }

        var lowered = channelName.ToLower();
        var channel = await db.Channels.AsNoTracking()
            .FirstOrDefaultAsync(c => c.TeamId == team.Id && c.Name.ToLower() == lowered, ct);
        if (channel is null)
        {
            return null;
        }

        var cm = await db.ChannelMembers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.ChannelId == channel.Id && m.UserId == userId, ct);
        return new ChannelAccess(team, member.Role, channel, cm is not null, cm?.CanWrite ?? false, cm?.CanManage ?? false);
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    private sealed record ChannelAccess(
        TeamEntity Team,
        MemberRole Role,
        ChannelEntity Channel,
        bool IsChannelMember,
        bool CanWrite,
        bool CanManage);
}
