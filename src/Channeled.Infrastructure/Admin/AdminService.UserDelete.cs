using System.Text.Json;
using Channeled.Application.Abstractions.Results;
using Channeled.Application.Admin;
using Channeled.Application.Identity;
using Channeled.Domain.Audit;
using Channeled.Domain.Errors;
using Channeled.Domain.Identity;
using Channeled.Domain.Teams;
using Channeled.Infrastructure.Files;
using Channeled.Infrastructure.Identity;
using Channeled.Infrastructure.Jobs;
using Channeled.Infrastructure.PublicLinks;
using Microsoft.EntityFrameworkCore;

namespace Channeled.Infrastructure.Admin;

public sealed partial class AdminService
{
    public const string PhaseNone = "none";
    public const string PhaseDisabled = "disabled";
    public const string PhaseTeams = "teams";
    public const string PhasePersonal = "personal";
    public const string PhaseMemberships = "memberships";

    public async Task<AppResult> EnableUserAsync(Guid actorId, Guid userId, RequestContext context, CancellationToken ct = default)
    {
        if (!await IsAdminAsync(actorId, ct))
        {
            return NotFound();
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var user = await LockUserAsync(userId, ct);
        if (user is null)
        {
            return NotFound();
        }

        if (user.DeletePhase is PhasePersonal or PhaseMemberships)
        {
            return AppResult.Fail(ErrorCodes.DeleteInProgress, "User delete has passed the point of no return.");
        }

        user.Status = "active";
        user.LockoutEnd = null;
        user.DeletePhase = PhaseNone;
        user.DeletePhaseChangedAt = null;
        await WriteAuditAsync(actorId, AuditEventType.UserDisable, "user", user.Id.ToString(), null, context, ct,
            """{"action":"enable","cancelledDelete":true}""");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return AppResult.Ok();
    }

    public async Task<AppResult<UserDeletePreviewDto>> GetUserDeletePreviewAsync(
        Guid actorId,
        Guid userId,
        CancellationToken ct = default)
    {
        if (!await IsAdminAsync(actorId, ct))
        {
            return NotFound<UserDeletePreviewDto>();
        }

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
        {
            return NotFound<UserDeletePreviewDto>();
        }

        return AppResult<UserDeletePreviewDto>.Ok(await BuildPreviewAsync(user, ct));
    }

    public async Task<AppResult> DeleteUserAsync(
        Guid actorId,
        Guid userId,
        DeleteUserRequest request,
        RequestContext context,
        CancellationToken ct = default)
    {
        if (!await IsAdminAsync(actorId, ct))
        {
            return NotFound();
        }

        if (actorId == userId)
        {
            return AppResult.Fail(ErrorCodes.Validation, "Cannot delete yourself.");
        }

        var actor = await userManager.FindByIdAsync(actorId.ToString());
        if (actor is null || string.IsNullOrEmpty(request.Password)
            || !await userManager.CheckPasswordAsync(actor, request.Password))
        {
            return AppResult.Fail(ErrorCodes.InvalidPassword, "Password is incorrect.");
        }

        AppResult last = AppResult.Ok();
        for (var i = 0; i < 12; i++)
        {
            var (done, result) = await AdvanceUserDeleteAsync(
                actorId, userId, request.TeamResolutions, context, ct, "admin");
            last = result;
            if (!result.Succeeded)
            {
                return result;
            }

            if (done)
            {
                return AppResult.Ok();
            }
        }

        return AppResult.Fail(ErrorCodes.Conflict, "User delete did not complete; retry or check delete-preview phase.");
    }

    public async Task<int> SweepStuckUserDeletesAsync(CancellationToken ct = default)
    {
        var grace = TimeSpan.FromSeconds(Math.Max(0, userDeleteOptions.Value.LatePhaseGraceSeconds));
        var cutoff = clock.UtcNow - grace;
        var stuck = await db.Users.AsNoTracking()
            .Where(u => (u.DeletePhase == PhasePersonal || u.DeletePhase == PhaseMemberships)
                        && u.DeletePhaseChangedAt != null
                        && u.DeletePhaseChangedAt <= cutoff)
            .Select(u => u.Id)
            .Take(20)
            .ToListAsync(ct);

        var n = 0;
        foreach (var id in stuck)
        {
            // Drive the same CAS steps to completion (personal → memberships → identity).
            for (var i = 0; i < 6; i++)
            {
                var (done, result) = await AdvanceUserDeleteAsync(
                    null, id, null, new RequestContext("", ""), ct, "sweep");
                if (!result.Succeeded)
                {
                    break;
                }

                if (done)
                {
                    n++;
                    break;
                }
            }
        }

        return n;
    }

    private async Task<(bool Done, AppResult Result)> AdvanceUserDeleteAsync(
        Guid? actorId,
        Guid userId,
        IReadOnlyList<DeleteUserTeamResolution>? resolutions,
        RequestContext context,
        CancellationToken ct,
        string completedBy)
    {
        // CAS updates DeletePhase via ExecuteUpdate; clear so LockUserAsync does not return a stale phase.
        db.ChangeTracker.Clear();
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Platform-admin deletes lock all admin rows in Id order first (avoids mutual-delete deadlock
        // and serializes the enabled-admin invariant).
        var targetIsAdmin = await db.Users.AsNoTracking()
            .AnyAsync(u => u.Id == userId && u.IsPlatformAdmin, ct);
        AppUser? user;
        if (targetIsAdmin)
        {
            await LockAllPlatformAdminsAsync(ct);
            user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        }
        else
        {
            user = await LockUserAsync(userId, ct);
        }

        if (user is null)
        {
            return (true, AppResult.Ok());
        }

        if (!await WouldLeaveEnabledAdminAsync(user, ct))
        {
            return (false, AppResult.Fail(ErrorCodes.LastPlatformAdmin, "Cannot delete the last enabled platform admin."));
        }

        var phase = NormalizePhase(user.DeletePhase);

        if (phase == PhaseNone)
        {
            user.Status = "disabled";
            user.LockoutEnabled = true;
            user.LockoutEnd = DateTimeOffset.MaxValue;
            await RevokeSessionsAsync(user.Id, ct);
            await db.SaveChangesAsync(ct);
            if (!await CasPhaseAsync(user.Id, PhaseNone, PhaseDisabled, ct))
            {
                await tx.RollbackAsync(ct);
                return (false, AppResult.Ok());
            }

            await WriteAuditAsync(actorId, AuditEventType.UserDisable, "user", user.Id.ToString(), null, context, ct,
                $"{{\"deletePhase\":\"disabled\",\"completedBy\":{JsonString(completedBy)}}}");
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return (false, AppResult.Ok());
        }

        if (phase == PhaseDisabled)
        {
            var sole = await ListSoleOwnedTeamsAsync(user.Id, ct);
            var prepare = ValidateResolutions(sole, resolutions);
            if (prepare is not null)
            {
                await tx.RollbackAsync(ct);
                return (false, prepare);
            }

            // Transfers inside this txn; team deletes after commit.
            foreach (var team in sole)
            {
                var res = resolutions!.First(r => r.TeamId == team.TeamId);
                if (string.Equals(res.Action, "transfer", StringComparison.OrdinalIgnoreCase))
                {
                    var transfer = await TransferOwnershipForDeleteAsync(user.Id, team.TeamId, res.NewOwnerUserId!.Value, actorId, context, ct);
                    if (!transfer.Succeeded)
                    {
                        await tx.RollbackAsync(ct);
                        return (false, transfer);
                    }
                }
            }

            if (!await CasPhaseAsync(user.Id, PhaseDisabled, PhaseTeams, ct))
            {
                await tx.RollbackAsync(ct);
                return (false, AppResult.Ok());
            }

            await WriteAuditAsync(actorId, AuditEventType.UserDelete, "user", user.Id.ToString(), null, context, ct,
                $"{{\"phase\":\"teams\",\"completedBy\":{JsonString(completedBy)}}}");
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            foreach (var team in sole)
            {
                var res = resolutions!.First(r => r.TeamId == team.TeamId);
                if (!string.Equals(res.Action, "delete", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Re-count caps at execute time.
                var fresh = (await ListSoleOwnedTeamsAsync(user.Id, ct)).FirstOrDefault(t => t.TeamId == team.TeamId);
                // Team may already have been transferred away if concurrent; skip missing.
                if (fresh is null)
                {
                    // Still owned? check team exists
                    if (!await db.Teams.AnyAsync(t => t.Id == team.TeamId, ct))
                    {
                        continue;
                    }

                    fresh = team;
                }

                if (fresh.OverTeamDeleteCap
                    || fresh.FileCount > channelDeleteOptions.Value.MaxFilesPerTeamDelete
                    || fresh.MessageCount > channelDeleteOptions.Value.MaxMessagesPerTeamDelete)
                {
                    // Roll phase back so admin can fix.
                    await CasPhaseAsync(user.Id, PhaseTeams, PhaseDisabled, ct);
                    return (false, AppResult.Fail(
                        ErrorCodes.TeamDeleteCapExceeded,
                        $"Team {fresh.Name} exceeds delete caps; transfer instead. Refresh the preview."));
                }

                var del = await teamService.DeleteTeamInternalAsync(actorId ?? Guid.Empty, team.TeamId, context, ct);
                if (!del.Succeeded)
                {
                    await CasPhaseAsync(user.Id, PhaseTeams, PhaseDisabled, ct);
                    return (false, del);
                }
            }

            return (false, AppResult.Ok());
        }

        if (phase == PhaseTeams)
        {
            await DiscardPersonalAsync(user, ct);
            if (!await CasPhaseAsync(user.Id, PhaseTeams, PhasePersonal, ct))
            {
                await tx.RollbackAsync(ct);
                return (false, AppResult.Ok());
            }

            await WriteAuditAsync(actorId, AuditEventType.UserDelete, "user", user.Id.ToString(), null, context, ct,
                $"{{\"phase\":\"personal\",\"completedBy\":{JsonString(completedBy)}}}");
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return (false, AppResult.Ok());
        }

        if (phase == PhasePersonal)
        {
            await StripMembershipsAsync(user.Id, actorId, context, ct);
            if (!await CasPhaseAsync(user.Id, PhasePersonal, PhaseMemberships, ct))
            {
                await tx.RollbackAsync(ct);
                return (false, AppResult.Ok());
            }

            await WriteAuditAsync(actorId, AuditEventType.UserDelete, "user", user.Id.ToString(), null, context, ct,
                $"{{\"phase\":\"memberships\",\"completedBy\":{JsonString(completedBy)}}}");
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return (false, AppResult.Ok());
        }

        if (phase == PhaseMemberships)
        {
            await WriteAuditAsync(actorId, AuditEventType.UserDelete, "user", user.Id.ToString(), null, context, ct,
                $"{{\"phase\":\"identity\",\"completedBy\":{JsonString(completedBy)}}}");
            await db.SaveChangesAsync(ct);
            var result = await userManager.DeleteAsync(user);
            if (!result.Succeeded)
            {
                await tx.RollbackAsync(ct);
                return (false, AppResult.Fail(ErrorCodes.Validation, string.Join("; ", result.Errors.Select(e => e.Description))));
            }

            await tx.CommitAsync(ct);
            return (true, AppResult.Ok());
        }

        await tx.CommitAsync(ct);
        return (true, AppResult.Ok());
    }

    private static AppResult? ValidateResolutions(
        IReadOnlyList<UserDeleteOwnedTeamDto> sole,
        IReadOnlyList<DeleteUserTeamResolution>? resolutions)
    {
        var map = (resolutions ?? Array.Empty<DeleteUserTeamResolution>()).ToDictionary(r => r.TeamId);
        if (sole.Count != map.Count || sole.Any(t => !map.ContainsKey(t.TeamId)))
        {
            return AppResult.Fail(ErrorCodes.DeleteResolutionsStale, "Sole-owned team resolutions are stale; refresh the preview.");
        }

        foreach (var team in sole)
        {
            var res = map[team.TeamId];
            var action = (res.Action ?? "").Trim().ToLowerInvariant();
            if (action == "delete")
            {
                if (team.TransferOnly || team.OverTeamDeleteCap)
                {
                    return AppResult.Fail(
                        ErrorCodes.TeamDeleteCapExceeded,
                        $"Team {team.Name} is too large to delete here; transfer instead.");
                }
            }
            else if (action == "transfer")
            {
                if (res.NewOwnerUserId is not Guid oid
                    || !team.EligibleOwners.Any(m => m.UserId == oid))
                {
                    return AppResult.Fail(ErrorCodes.DeleteResolutionsStale, "Transfer target is invalid; refresh the preview.");
                }
            }
            else
            {
                return AppResult.Fail(ErrorCodes.Validation, "action must be transfer or delete.");
            }
        }

        return null;
    }

    private async Task<AppResult> TransferOwnershipForDeleteAsync(
        Guid fromUserId,
        Guid teamId,
        Guid newOwnerUserId,
        Guid? actorId,
        RequestContext context,
        CancellationToken ct)
    {
        var member = await db.TeamMembers.FirstOrDefaultAsync(m => m.TeamId == teamId && m.UserId == newOwnerUserId, ct);
        var targetUser = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == newOwnerUserId, ct);
        if (member is null || targetUser is null || targetUser.Status != "active")
        {
            return AppResult.Fail(ErrorCodes.DeleteResolutionsStale, "Transfer target is invalid; refresh the preview.");
        }

        var current = await db.TeamMembers.FirstAsync(m => m.TeamId == teamId && m.UserId == fromUserId, ct);
        current.Role = MemberRole.Admin;
        member.Role = MemberRole.Owner;
        await WriteAuditAsync(actorId, AuditEventType.RoleChange, "user", newOwnerUserId.ToString(), teamId, context, ct,
            """{"reason":"user_delete_transfer"}""");
        await db.SaveChangesAsync(ct);
        return AppResult.Ok();
    }

    private async Task DiscardPersonalAsync(AppUser user, CancellationToken ct)
    {
        var chunk = channelDeleteOptions.Value.ObjectDeleteChunkSize;
        var files = await db.Files
            .Where(f => f.OwnerUserId == user.Id)
            .Select(f => new { f.Id, f.StorageKey })
            .ToListAsync(ct);
        var ids = files.Select(f => f.Id).ToList();
        var keys = files.Select(f => f.StorageKey).ToList();
        if (ids.Count > 0)
        {
            await db.FileStars.Where(s => ids.Contains(s.FileId)).ExecuteDeleteAsync(ct);
            await db.Files.Where(f => f.OwnerUserId == user.Id).ExecuteDeleteAsync(ct);
        }

        await db.Folders.Where(f => f.OwnerUserId == user.Id).ExecuteDeleteAsync(ct);
        await db.TrashBatches.Where(b => b.OwnerUserId == user.Id).ExecuteDeleteAsync(ct);

        if (!string.IsNullOrEmpty(user.AvatarStorageKey))
        {
            keys.Add(user.AvatarStorageKey);
            user.AvatarStorageKey = null;
            user.AvatarContentType = null;
            user.AvatarUpdatedAt = null;
        }

        user.PersonalUsageBytes = 0;
        ObjectDeleteOutbox.Enqueue(db, clock.UtcNow, keys, multiparts: null, chunk);
    }

    private async Task StripMembershipsAsync(Guid userId, Guid? actorId, RequestContext context, CancellationToken ct)
    {
        await db.TeamMembers.Where(m => m.UserId == userId).ExecuteDeleteAsync(ct);
        await db.ChannelMembers.Where(m => m.UserId == userId).ExecuteDeleteAsync(ct);
        await db.TeamJoinRequests.Where(r => r.UserId == userId).ExecuteDeleteAsync(ct);
        await db.ChannelJoinRequests.Where(r => r.UserId == userId).ExecuteDeleteAsync(ct);
        await db.Notifications.Where(n => n.UserId == userId).ExecuteDeleteAsync(ct);
        await ShareLinkRevoker.RevokeAsync(
            db, clock, l => l.CreatedBy == userId, "user_deleted", actorId, context, ct);
        await RevokeSessionsAsync(userId, ct);
    }

    private async Task RevokeSessionsAsync(Guid userId, CancellationToken ct) =>
        await db.UserSessions.Where(s => s.UserId == userId).ExecuteDeleteAsync(ct);

    private async Task<bool> WouldLeaveEnabledAdminAsync(AppUser target, CancellationToken ct)
    {
        if (!target.IsPlatformAdmin)
        {
            return true;
        }

        var enabledAdmins = await db.Users.CountAsync(
            u => u.IsPlatformAdmin
                 && u.Status == "active"
                 && (u.LockoutEnd == null || u.LockoutEnd <= clock.UtcNow),
            ct);
        var targetCounted = target.Status == "active"
            && (target.LockoutEnd == null || target.LockoutEnd <= clock.UtcNow);
        return targetCounted ? enabledAdmins >= 2 : enabledAdmins >= 1;
    }

    private async Task<AppUser?> LockUserAsync(Guid userId, CancellationToken ct)
    {
        var rows = await db.Database
            .SqlQuery<Guid>($"""SELECT "Id" AS "Value" FROM "AspNetUsers" WHERE "Id" = {userId} FOR UPDATE""")
            .ToListAsync(ct);
        if (rows.Count == 0)
        {
            return null;
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is not null)
        {
            await db.Entry(user).ReloadAsync(ct);
        }

        return user;
    }

    private async Task LockAllPlatformAdminsAsync(CancellationToken ct)
    {
        // Ascending Id order avoids deadlocks when two admins delete each other.
        _ = await db.Database
            .SqlQuery<Guid>($"""
                SELECT "Id" AS "Value" FROM "AspNetUsers"
                WHERE "IsPlatformAdmin" = TRUE
                ORDER BY "Id"
                FOR UPDATE
                """)
            .ToListAsync(ct);
    }

    private async Task<bool> CasPhaseAsync(Guid userId, string expected, string next, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var n = await db.Users
            .Where(u => u.Id == userId && u.DeletePhase == expected)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(u => u.DeletePhase, next)
                    .SetProperty(u => u.DeletePhaseChangedAt, now),
                ct);
        if (n == 1)
        {
            var tracked = db.ChangeTracker.Entries<AppUser>().FirstOrDefault(e => e.Entity.Id == userId);
            if (tracked is not null)
            {
                tracked.Entity.DeletePhase = next;
                tracked.Entity.DeletePhaseChangedAt = now;
                tracked.State = EntityState.Unchanged;
            }
        }

        return n == 1;
    }

    private async Task<UserDeletePreviewDto> BuildPreviewAsync(AppUser user, CancellationToken ct)
    {
        var sole = await ListSoleOwnedTeamsAsync(user.Id, ct);
        var personalCount = await db.Files.CountAsync(f => f.OwnerUserId == user.Id, ct);
        var personalBytes = await db.Files.Where(f => f.OwnerUserId == user.Id).SumAsync(f => (long?)f.SizeBytes, ct) ?? 0;
        var enabledAdmins = await db.Users.CountAsync(
            u => u.IsPlatformAdmin
                 && u.Status == "active"
                 && (u.LockoutEnd == null || u.LockoutEnd <= clock.UtcNow),
            ct);
        var targetCounted = user.IsPlatformAdmin && user.Status == "active"
            && (user.LockoutEnd == null || user.LockoutEnd <= clock.UtcNow);
        var isLast = user.IsPlatformAdmin && (targetCounted ? enabledAdmins <= 1 : enabledAdmins < 1);
        var phase = NormalizePhase(user.DeletePhase);
        return new UserDeletePreviewDto(
            user.Id,
            user.Email ?? "",
            phase,
            isLast,
            phase is PhaseNone or PhaseDisabled or PhaseTeams,
            personalCount,
            personalBytes,
            !string.IsNullOrEmpty(user.AvatarStorageKey),
            sole);
    }

    private async Task<List<UserDeleteOwnedTeamDto>> ListSoleOwnedTeamsAsync(Guid userId, CancellationToken ct)
    {
        var opts = channelDeleteOptions.Value;
        var ownedTeamIds = await db.TeamMembers.AsNoTracking()
            .Where(m => m.UserId == userId && m.Role == MemberRole.Owner)
            .Select(m => m.TeamId)
            .ToListAsync(ct);

        var result = new List<UserDeleteOwnedTeamDto>();
        foreach (var teamId in ownedTeamIds)
        {
            var ownerCount = await db.TeamMembers.CountAsync(m => m.TeamId == teamId && m.Role == MemberRole.Owner, ct);
            if (ownerCount != 1)
            {
                continue;
            }

            var team = await db.Teams.AsNoTracking().FirstAsync(t => t.Id == teamId, ct);
            var channelIds = await db.Channels.AsNoTracking().Where(c => c.TeamId == teamId).Select(c => c.Id).ToListAsync(ct);
            var fileCount = channelIds.Count == 0
                ? 0
                : await db.Files.CountAsync(f => f.ChannelId != null && channelIds.Contains(f.ChannelId.Value), ct);
            var messageCount = channelIds.Count == 0
                ? 0
                : await db.ChannelMessages.CountAsync(m => channelIds.Contains(m.ChannelId), ct);

            var over = fileCount > opts.MaxFilesPerTeamDelete || messageCount > opts.MaxMessagesPerTeamDelete;
            if (!over)
            {
                foreach (var cid in channelIds)
                {
                    var fc = await db.Files.CountAsync(f => f.ChannelId == cid, ct);
                    var mc = await db.ChannelMessages.CountAsync(m => m.ChannelId == cid, ct);
                    if (fc > opts.MaxFilesPerTxn || mc > opts.MaxMessagesPerTxn)
                    {
                        over = true;
                        break;
                    }
                }
            }

            var members = await (
                from m in db.TeamMembers.AsNoTracking()
                join u in db.Users.AsNoTracking() on m.UserId equals u.Id
                where m.TeamId == teamId && m.UserId != userId && u.Status == "active"
                select new UserDeleteEligibleMemberDto(u.Id, u.Email ?? "", u.DisplayName)
            ).ToListAsync(ct);

            result.Add(new UserDeleteOwnedTeamDto(
                team.Id,
                team.Name,
                team.Slug,
                fileCount,
                messageCount,
                over,
                over,
                members));
        }

        return result;
    }

    private static string NormalizePhase(string? phase) =>
        phase switch
        {
            PhaseDisabled or PhaseTeams or PhasePersonal or PhaseMemberships => phase,
            _ => PhaseNone,
        };

    private static string JsonString(string? value) =>
        value is null ? "null" : JsonSerializer.Serialize(value);
}
