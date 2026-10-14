using Channeled.Application.Abstractions.Results;
using Channeled.Application.Abstractions.Storage;
using Channeled.Application.Abstractions.Time;
using Channeled.Application.Identity;
using Channeled.Domain.Audit;
using Channeled.Domain.Errors;
using Channeled.Domain.Identity;
using Channeled.Domain.Storage;
using Channeled.Infrastructure.Jobs;
using Channeled.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Channeled.Infrastructure.Identity;

public sealed class ProfileService(
    ApplicationDbContext db,
    UserManager<AppUser> userManager,
    IFileStore store,
    IClock clock,
    IOptionsMonitor<AccountOptions> accountOptions,
    ILogger<ProfileService> logger) : IProfileService
{
    public async Task<AppResult<AuthUserDto>> UpdateProfileAsync(
        Guid userId,
        UpdateProfileRequest request,
        RequestContext context,
        CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null || user.Status != "active")
        {
            return AppResult<AuthUserDto>.Fail(ErrorCodes.NotFound, "User not found.");
        }

        var changed = false;
        if (request.DisplayName is not null)
        {
            if (!DisplayNameRules.TryValidate(request.DisplayName, out var cleaned, out var err))
            {
                return AppResult<AuthUserDto>.Fail(ErrorCodes.Validation, err!);
            }

            if (!string.Equals(user.DisplayName, cleaned, StringComparison.Ordinal))
            {
                user.DisplayName = cleaned;
                changed = true;
            }
        }

        if (request.Username is not null)
        {
            var usernameResult = await TrySetUsernameAsync(user, request.Username, ct);
            if (!usernameResult.Succeeded)
            {
                return AppResult<AuthUserDto>.Fail(usernameResult.Code!, usernameResult.Message!);
            }

            changed = changed || usernameResult.Value;
        }

        if (request.Bio is not null)
        {
            if (!BioRules.TryValidate(request.Bio, out var bio, out var bioErr))
            {
                return AppResult<AuthUserDto>.Fail(ErrorCodes.Validation, bioErr!);
            }

            if (!string.Equals(user.Bio, bio, StringComparison.Ordinal))
            {
                user.Bio = bio;
                changed = true;
            }
        }

        if (changed)
        {
            await userManager.UpdateAsync(user);
            await WriteAuditAsync(userId, AuditEventType.UsernameChange, "user", userId.ToString(), context, ct);
            await db.SaveChangesAsync(ct);
        }

        return AppResult<AuthUserDto>.Ok(AuthMapping.ToDto(user));
    }

    public async Task<AppResult<UsernameAvailabilityDto>> CheckUsernameAsync(
        Guid userId,
        string username,
        CancellationToken ct = default)
    {
        if (!UsernameRules.TryValidate(username, out var normalized, out var error))
        {
            return AppResult<UsernameAvailabilityDto>.Ok(new UsernameAvailabilityDto(false, error));
        }

        var taken = await IsUsernameTakenAsync(normalized, userId, ct);
        return AppResult<UsernameAvailabilityDto>.Ok(
            taken
                ? new UsernameAvailabilityDto(false, "That username is taken.")
                : new UsernameAvailabilityDto(true, null));
    }

    public async Task<AppResult<UserCardDto>> GetCardAsync(Guid viewerId, Guid targetUserId, CancellationToken ct = default)
    {
        var viewer = await userManager.FindByIdAsync(viewerId.ToString());
        if (viewer is null)
        {
            return AppResult<UserCardDto>.Fail(ErrorCodes.Unauthorized, "Not authenticated.");
        }

        var allowed = await UserVisibility.CanViewProfileAsync(db, viewerId, targetUserId, viewer.IsPlatformAdmin, ct);
        if (!allowed)
        {
            return AppResult<UserCardDto>.Fail(ErrorCodes.NotFound, "User not found.");
        }

        var target = await userManager.FindByIdAsync(targetUserId.ToString());
        if (target is null || target.Status != "active")
        {
            return AppResult<UserCardDto>.Fail(ErrorCodes.NotFound, "User not found.");
        }

        var showEmail = viewerId == targetUserId || viewer.IsPlatformAdmin;
        return AppResult<UserCardDto>.Ok(new UserCardDto(
            target.Id,
            target.DisplayName,
            target.UsernameSlug,
            target.AvatarUpdatedAt?.ToUnixTimeMilliseconds(),
            showEmail ? target.Email : null,
            string.IsNullOrEmpty(target.Bio) ? null : target.Bio));
    }

    public async Task<AppResult<AvatarOpenResult>> OpenAvatarAsync(
        Guid viewerId,
        Guid targetUserId,
        CancellationToken ct = default)
    {
        var viewer = await userManager.FindByIdAsync(viewerId.ToString());
        if (viewer is null)
        {
            return AppResult<AvatarOpenResult>.Fail(ErrorCodes.Unauthorized, "Not authenticated.");
        }

        var allowed = await UserVisibility.CanViewProfileAsync(db, viewerId, targetUserId, viewer.IsPlatformAdmin, ct);
        if (!allowed)
        {
            return AppResult<AvatarOpenResult>.Fail(ErrorCodes.NotFound, "User not found.");
        }

        var target = await userManager.FindByIdAsync(targetUserId.ToString());
        if (target?.AvatarStorageKey is null)
        {
            return AppResult<AvatarOpenResult>.Fail(ErrorCodes.NotFound, "User not found.");
        }

        var stream = await store.OpenReadAsync(target.AvatarStorageKey, cancellationToken: ct);
        return AppResult<AvatarOpenResult>.Ok(new AvatarOpenResult(
            stream,
            target.AvatarContentType ?? "image/jpeg",
            target.AvatarUpdatedAt));
    }

    public async Task<AppResult<AuthUserDto>> UploadAvatarAsync(
        Guid userId,
        Stream content,
        long? contentLength,
        RequestContext context,
        CancellationToken ct = default)
    {
        if (contentLength is > AvatarImageProcessor.MaxBytes)
        {
            return AppResult<AuthUserDto>.Fail(ErrorCodes.Validation, "Avatar must be 2 MB or smaller.");
        }

        var processed = await AvatarImageProcessor.ProcessAsync(content, contentLength, ct);
        if (!processed.Succeeded)
        {
            return AppResult<AuthUserDto>.Fail(processed.Code ?? ErrorCodes.Validation, processed.Message ?? "Invalid image.");
        }

        var (jpeg, contentType) = processed.Value!;

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return AppResult<AuthUserDto>.Fail(ErrorCodes.NotFound, "User not found.");
        }

        var oldKey = user.AvatarStorageKey;
        var blobId = UuidV7.NewGuid();
        var key = ObjectKey.ForUserAvatar(userId, blobId);
        await using (var ms = new MemoryStream(jpeg))
        {
            await store.PutAsync(key, ms, contentType, ct);
        }

        user.AvatarStorageKey = key;
        user.AvatarContentType = contentType;
        user.AvatarUpdatedAt = clock.UtcNow;
        await userManager.UpdateAsync(user);

        if (!string.IsNullOrWhiteSpace(oldKey))
        {
            ObjectDeleteOutbox.Enqueue(db, clock.UtcNow, [oldKey], null, 2000);
        }

        await WriteAuditAsync(userId, AuditEventType.AvatarChange, "user", userId.ToString(), context, ct, """{"action":"upload"}""");
        await db.SaveChangesAsync(ct);
        return AppResult<AuthUserDto>.Ok(AuthMapping.ToDto(user));
    }

    public async Task<AppResult> DeleteAvatarAsync(Guid userId, RequestContext context, CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return AppResult.Fail(ErrorCodes.NotFound, "User not found.");
        }

        if (string.IsNullOrWhiteSpace(user.AvatarStorageKey))
        {
            return AppResult.Ok();
        }

        var oldKey = user.AvatarStorageKey;
        user.AvatarStorageKey = null;
        user.AvatarContentType = null;
        user.AvatarUpdatedAt = null;
        await userManager.UpdateAsync(user);
        ObjectDeleteOutbox.Enqueue(db, clock.UtcNow, [oldKey!], null, 2000);
        await WriteAuditAsync(userId, AuditEventType.AvatarChange, "user", userId.ToString(), context, ct, """{"action":"delete"}""");
        await db.SaveChangesAsync(ct);
        return AppResult.Ok();
    }

    private async Task<AppResult<bool>> TrySetUsernameAsync(AppUser user, string username, CancellationToken ct)
    {
        if (!UsernameRules.TryValidate(username, out var normalized, out var error))
        {
            return AppResult<bool>.Fail(ErrorCodes.Validation, error!);
        }

        if (string.Equals(user.UsernameSlug, normalized, StringComparison.Ordinal))
        {
            return AppResult<bool>.Ok(false);
        }

        var cooldownDays = Math.Max(1, accountOptions.CurrentValue.UsernameCooldownDays);
        var cooldown = TimeSpan.FromDays(cooldownDays);
        var isReclaim = await IsReclaimAsync(user.Id, normalized, ct);
        if (user.HasChangedUsername
            && user.UsernameChangedAt is { } last
            && clock.UtcNow - last < cooldown
            && !isReclaim)
        {
            return AppResult<bool>.Fail(ErrorCodes.Validation, "Username can only be changed once every 30 days.");
        }

        if (await IsUsernameTakenAsync(normalized, user.Id, ct))
        {
            return AppResult<bool>.Fail(ErrorCodes.Conflict, "That username is taken.");
        }

        var old = user.UsernameSlug;
        if (!string.IsNullOrWhiteSpace(old) && !string.Equals(old, normalized, StringComparison.Ordinal))
        {
            db.UsernameReservations.Add(new UsernameReservation
            {
                NormalizedUsername = old,
                PreviousOwnerUserId = user.Id,
                ExpiresAt = clock.UtcNow.Add(cooldown),
                Reason = "cooldown",
            });
        }

        var ownHold = await db.UsernameReservations
            .FirstOrDefaultAsync(r => r.NormalizedUsername == normalized && r.PreviousOwnerUserId == user.Id, ct);
        if (ownHold is not null)
        {
            db.UsernameReservations.Remove(ownHold);
        }

        user.UsernameSlug = normalized;
        if (!user.HasChangedUsername)
        {
            user.HasChangedUsername = true;
            user.UsernameChangedAt = clock.UtcNow;
        }
        else if (!isReclaim)
        {
            // Reclaim does not reset the 30-day cooldown.
            user.UsernameChangedAt = clock.UtcNow;
        }

        return AppResult<bool>.Ok(true);
    }

    private async Task<bool> IsReclaimAsync(Guid userId, string normalized, CancellationToken ct) =>
        await db.UsernameReservations.AsNoTracking()
            .AnyAsync(
                r => r.NormalizedUsername == normalized
                     && r.PreviousOwnerUserId == userId
                     && r.ExpiresAt > clock.UtcNow,
                ct);

    private async Task<bool> IsUsernameTakenAsync(string normalized, Guid exceptUserId, CancellationToken ct)
    {
        if (await userManager.Users.AsNoTracking()
                .AnyAsync(u => u.UsernameSlug == normalized && u.Id != exceptUserId, ct))
        {
            return true;
        }

        return await db.UsernameReservations.AsNoTracking()
            .AnyAsync(
                r => r.NormalizedUsername == normalized
                     && r.ExpiresAt > clock.UtcNow
                     && r.PreviousOwnerUserId != exceptUserId,
                ct);
    }

    private async Task WriteAuditAsync(
        Guid? actorId,
        AuditEventType type,
        string targetType,
        string targetId,
        RequestContext context,
        CancellationToken ct,
        string detailsJson = "{}")
    {
        db.AuditEvents.Add(new AuditEvent
        {
            Id = UuidV7.NewGuid(),
            OccurredAt = clock.UtcNow,
            ActorId = actorId,
            EventType = type,
            TargetType = targetType,
            TargetId = targetId,
            DetailsJson = detailsJson,
            Ip = Truncate(context.Ip, 64),
            UserAgent = Truncate(context.UserAgent, 512),
        });
        await Task.CompletedTask;
        _ = logger;
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
