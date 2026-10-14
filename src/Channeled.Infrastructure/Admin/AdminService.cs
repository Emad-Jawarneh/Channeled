using System.Globalization;
using System.Text;
using Channeled.Application.Abstractions.Operations;
using Channeled.Application.Abstractions.Results;
using Channeled.Application.Abstractions.Storage;
using Channeled.Application.Abstractions.Time;
using Channeled.Application.Admin;
using Channeled.Application.Identity;
using Channeled.Domain.Audit;
using Channeled.Domain.Errors;
using Channeled.Domain.Identity;
using Channeled.Infrastructure.Files;
using Channeled.Infrastructure.Identity;
using Channeled.Infrastructure.Persistence;
using Channeled.Infrastructure.PublicLinks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Channeled.Infrastructure.Admin;

public sealed class AdminService(
    ApplicationDbContext db,
    UserManager<AppUser> userManager,
    IClock clock,
    IFileStore store,
    IBackupService backups,
    ISystemHealthService health,
    IOptions<BackupOptions> backupOptions) : IAdminService
{
    public async Task<AppResult<AdminDashboardDto>> GetDashboardAsync(Guid actorId, CancellationToken ct = default)
    {
        if (!await IsAdminAsync(actorId, ct))
        {
            return NotFound<AdminDashboardDto>();
        }

        var since = clock.UtcNow.AddHours(-24);
        var dto = new AdminDashboardDto(
            await db.Users.CountAsync(ct),
            await db.Users.CountAsync(u => u.Status == "active", ct),
            await db.Teams.CountAsync(ct),
            await db.Channels.CountAsync(ct),
            await db.Files.CountAsync(f => f.DeletedAt == null, ct),
            await db.Files.Where(f => f.DeletedAt == null).SumAsync(f => (long?)f.SizeBytes, ct) ?? 0,
            await db.AuditEvents.CountAsync(a => a.OccurredAt >= since, ct));
        return AppResult<AdminDashboardDto>.Ok(dto);
    }

    public async Task<AppResult<AdminHealthDto>> GetHealthAsync(Guid actorId, CancellationToken ct = default)
    {
        if (!await IsAdminAsync(actorId, ct))
        {
            return NotFound<AdminHealthDto>();
        }

        var report = await health.CheckAsync(ct);
        return AppResult<AdminHealthDto>.Ok(new AdminHealthDto(
            report.Status,
            report.Database,
            report.Store,
            report.Disk,
            report.DiskFreeBytes,
            report.StoreKind,
            report.PendingJobs,
            report.DeadJobs,
            report.Detail));
    }

    public async Task<AppResult<IReadOnlyList<AdminUserDto>>> ListUsersAsync(Guid actorId, CancellationToken ct = default)
    {
        if (!await IsAdminAsync(actorId, ct))
        {
            return NotFound<IReadOnlyList<AdminUserDto>>();
        }

        var users = await db.Users.AsNoTracking().OrderBy(u => u.Email).ToListAsync(ct);
        IReadOnlyList<AdminUserDto> list = users.Select(ToUserDto).ToList();
        return AppResult<IReadOnlyList<AdminUserDto>>.Ok(list);
    }

    public async Task<AppResult<AdminUserDto>> GetUserAsync(Guid actorId, Guid userId, CancellationToken ct = default)
    {
        if (!await IsAdminAsync(actorId, ct))
        {
            return NotFound<AdminUserDto>();
        }

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        return user is null ? NotFound<AdminUserDto>() : AppResult<AdminUserDto>.Ok(ToUserDto(user));
    }

    public async Task<AppResult<AdminUserDto>> CreateUserAsync(
        Guid actorId,
        CreateAdminUserRequest request,
        RequestContext context,
        CancellationToken ct = default)
    {
        if (!await IsAdminAsync(actorId, ct))
        {
            return NotFound<AdminUserDto>();
        }

        if (!DisplayNameRules.TryValidate(request.DisplayName, out var displayName, out var displayErr))
        {
            return AppResult<AdminUserDto>.Fail(ErrorCodes.Validation, displayErr!);
        }

        var user = new AppUser
        {
            Id = UuidV7.NewGuid(),
            Email = request.Email.Trim(),
            UserName = request.Email.Trim(),
            DisplayName = displayName,
            UsernameSlug = await UsernameAllocator.AllocateUniqueAsync(userManager, db, displayName, ct),
            EmailConfirmed = true,
            Status = "active",
            IsPlatformAdmin = request.IsPlatformAdmin,
            PersonalQuotaBytes = request.PersonalQuotaBytes ?? 20L * 1024 * 1024 * 1024,
        };
        var created = await userManager.CreateAsync(user, request.Password);
        if (!created.Succeeded)
        {
            return AppResult<AdminUserDto>.Fail(ErrorCodes.Validation, string.Join("; ", created.Errors.Select(e => e.Description)));
        }

        await WriteAuditAsync(actorId, AuditEventType.UserCreate, "user", user.Id.ToString(), null, context, ct);
        await db.SaveChangesAsync(ct);
        return AppResult<AdminUserDto>.Ok(ToUserDto(user));
    }

    public async Task<AppResult<AdminUserDto>> UpdateUserAsync(
        Guid actorId,
        Guid userId,
        UpdateAdminUserRequest request,
        RequestContext context,
        CancellationToken ct = default)
    {
        if (!await IsAdminAsync(actorId, ct))
        {
            return NotFound<AdminUserDto>();
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
        {
            return NotFound<AdminUserDto>();
        }

        if (request.DisplayName is not null)
        {
            user.DisplayName = request.DisplayName.Trim();
        }

        if (request.IsPlatformAdmin is bool admin)
        {
            user.IsPlatformAdmin = admin;
        }

        if (request.PersonalQuotaBytes is long quota)
        {
            user.PersonalQuotaBytes = quota;
            await WriteAuditAsync(actorId, AuditEventType.QuotaChange, "user", user.Id.ToString(), null, context, ct,
                $"{{\"personalQuotaBytes\":{quota}}}");
        }

        await db.SaveChangesAsync(ct);
        return AppResult<AdminUserDto>.Ok(ToUserDto(user));
    }

    public async Task<AppResult> DisableUserAsync(Guid actorId, Guid userId, RequestContext context, CancellationToken ct = default)
    {
        if (!await IsAdminAsync(actorId, ct))
        {
            return NotFound();
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
        {
            return NotFound();
        }

        user.Status = "disabled";
        user.LockoutEnabled = true;
        user.LockoutEnd = DateTimeOffset.MaxValue;
        await WriteAuditAsync(actorId, AuditEventType.UserDisable, "user", user.Id.ToString(), null, context, ct);
        await db.SaveChangesAsync(ct);
        return AppResult.Ok();
    }

    public async Task<AppResult> DeleteUserAsync(Guid actorId, Guid userId, RequestContext context, CancellationToken ct = default)
    {
        if (!await IsAdminAsync(actorId, ct))
        {
            return NotFound();
        }

        if (actorId == userId)
        {
            return AppResult.Fail(ErrorCodes.Validation, "Cannot delete yourself.");
        }

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return NotFound();
        }

        await WriteAuditAsync(actorId, AuditEventType.UserDelete, "user", user.Id.ToString(), null, context, ct);
        await db.SaveChangesAsync(ct);
        var result = await userManager.DeleteAsync(user);
        return result.Succeeded
            ? AppResult.Ok()
            : AppResult.Fail(ErrorCodes.Validation, string.Join("; ", result.Errors.Select(e => e.Description)));
    }

    public async Task<AppResult> ResetUserPasswordAsync(
        Guid actorId,
        Guid userId,
        ResetUserPasswordRequest request,
        RequestContext context,
        CancellationToken ct = default)
    {
        if (!await IsAdminAsync(actorId, ct))
        {
            return NotFound();
        }

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return NotFound();
        }

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var reset = await userManager.ResetPasswordAsync(user, token, request.NewPassword);
        if (!reset.Succeeded)
        {
            return AppResult.Fail(ErrorCodes.Validation, string.Join("; ", reset.Errors.Select(e => e.Description)));
        }

        await WriteAuditAsync(actorId, AuditEventType.PasswordChange, "user", user.Id.ToString(), null, context, ct,
            """{"source":"admin_reset"}""");
        await db.SaveChangesAsync(ct);
        return AppResult.Ok();
    }

    public async Task<AppResult<IReadOnlyList<AdminTeamDto>>> ListTeamsAsync(Guid actorId, CancellationToken ct = default)
    {
        if (!await IsAdminAsync(actorId, ct))
        {
            return NotFound<IReadOnlyList<AdminTeamDto>>();
        }

        var teams = await db.Teams.AsNoTracking().OrderBy(t => t.Name).ToListAsync(ct);
        var counts = await db.TeamMembers.AsNoTracking()
            .GroupBy(m => m.TeamId)
            .Select(g => new { TeamId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TeamId, x => x.Count, ct);

        IReadOnlyList<AdminTeamDto> list = teams.Select(t => new AdminTeamDto(
            t.Id, t.Name, t.Slug, t.QuotaBytes, t.UsageBytes, counts.GetValueOrDefault(t.Id), t.ArchivedAt)).ToList();
        return AppResult<IReadOnlyList<AdminTeamDto>>.Ok(list);
    }

    public async Task<AppResult<AdminTeamDto>> UpdateTeamQuotaAsync(
        Guid actorId,
        Guid teamId,
        UpdateTeamQuotaRequest request,
        RequestContext context,
        CancellationToken ct = default)
    {
        if (!await IsAdminAsync(actorId, ct))
        {
            return NotFound<AdminTeamDto>();
        }

        var team = await db.Teams.FirstOrDefaultAsync(t => t.Id == teamId, ct);
        if (team is null)
        {
            return NotFound<AdminTeamDto>();
        }

        team.QuotaBytes = request.QuotaBytes;
        await WriteAuditAsync(actorId, AuditEventType.QuotaChange, "team", team.Id.ToString(), team.Id, context, ct,
            $"{{\"quotaBytes\":{request.QuotaBytes}}}");
        await db.SaveChangesAsync(ct);
        var members = await db.TeamMembers.CountAsync(m => m.TeamId == team.Id, ct);
        return AppResult<AdminTeamDto>.Ok(new AdminTeamDto(
            team.Id, team.Name, team.Slug, team.QuotaBytes, team.UsageBytes, members, team.ArchivedAt));
    }

    public async Task<AppResult<AuditListResponse>> ListAuditAsync(Guid actorId, AuditListQuery query, CancellationToken ct = default)
    {
        if (!await IsAdminAsync(actorId, ct))
        {
            return NotFound<AuditListResponse>();
        }

        var limit = Math.Clamp(query.Limit <= 0 ? 50 : query.Limit, 1, 200);
        var q = db.AuditEvents.AsNoTracking().AsQueryable();
        if (query.EventType is AuditEventType type)
        {
            q = q.Where(a => a.EventType == type);
        }

        if (query.ActorId is Guid actor)
        {
            q = q.Where(a => a.ActorId == actor);
        }

        if (query.TeamId is Guid teamId)
        {
            q = q.Where(a => a.TeamId == teamId);
        }

        if (!string.IsNullOrWhiteSpace(query.TargetType))
        {
            q = q.Where(a => a.TargetType == query.TargetType);
        }

        if (query.From is DateTimeOffset from)
        {
            q = q.Where(a => a.OccurredAt >= from);
        }

        if (query.To is DateTimeOffset to)
        {
            q = q.Where(a => a.OccurredAt <= to);
        }

        if (!string.IsNullOrWhiteSpace(query.Cursor) && TryDecodeCursor(query.Cursor, out var cursorTime, out var cursorId))
        {
            q = q.Where(a => a.OccurredAt < cursorTime || (a.OccurredAt == cursorTime && a.Id.CompareTo(cursorId) < 0));
        }

        var rows = await q.OrderByDescending(a => a.OccurredAt).ThenByDescending(a => a.Id).Take(limit + 1).ToListAsync(ct);
        string? next = null;
        if (rows.Count > limit)
        {
            rows = rows.Take(limit).ToList();
            next = EncodeCursor(rows[^1].OccurredAt, rows[^1].Id);
        }

        IReadOnlyList<AuditEventDto> items = rows.Select(ToAuditDto).ToList();
        return AppResult<AuditListResponse>.Ok(new AuditListResponse(items, next));
    }

    public async Task<AppResult<string>> ExportAuditAsync(
        Guid actorId,
        AuditListQuery query,
        string format,
        CancellationToken ct = default)
    {
        var list = await ListAuditAsync(actorId, query with { Limit = 200, Cursor = null }, ct);
        if (!list.Succeeded || list.Value is null)
        {
            return AppResult<string>.Fail(list.Code ?? ErrorCodes.NotFound, list.Message ?? "Not found.");
        }

        // Export up to a few pages for M6
        var all = list.Value.Items.ToList();
        var cursor = list.Value.NextCursor;
        while (cursor is not null && all.Count < 2000)
        {
            var page = await ListAuditAsync(actorId, query with { Limit = 200, Cursor = cursor }, ct);
            if (!page.Succeeded || page.Value is null || page.Value.Items.Count == 0)
            {
                break;
            }

            all.AddRange(page.Value.Items);
            cursor = page.Value.NextCursor;
        }

        if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
        {
            var sb = new StringBuilder();
            sb.AppendLine("id,occurredAt,actorId,eventType,teamId,targetType,targetId,ip");
            foreach (var a in all)
            {
                sb.Append(CultureInfo.InvariantCulture, $"{a.Id},{a.OccurredAt:O},{a.ActorId},{a.EventType},{a.TeamId},{EscapeCsv(a.TargetType)},{EscapeCsv(a.TargetId)},{EscapeCsv(a.Ip)}");
                sb.AppendLine();
            }

            return AppResult<string>.Ok(sb.ToString());
        }

        var json = System.Text.Json.JsonSerializer.Serialize(all);
        return AppResult<string>.Ok(json);
    }

    public async Task<AppResult<IReadOnlyList<AdminBackupDto>>> ListBackupsAsync(Guid actorId, CancellationToken ct = default)
    {
        if (!await IsAdminAsync(actorId, ct))
        {
            return NotFound<IReadOnlyList<AdminBackupDto>>();
        }

        var list = (await backups.ListAsync(ct))
            .Select(b => new AdminBackupDto(b.Id, b.Id.ToString("N"), b.Kind, b.CreatedAt, b.State, b.VerifiedAt, b.ObjectCount, b.TotalBytes))
            .ToList();
        return AppResult<IReadOnlyList<AdminBackupDto>>.Ok(list);
    }

    public async Task<AppResult<AdminBackupDto>> RunBackupAsync(Guid actorId, RequestContext context, CancellationToken ct = default)
    {
        if (!await IsAdminAsync(actorId, ct))
        {
            return NotFound<AdminBackupDto>();
        }

        var info = await backups.CreateAsync("ondemand", ct);
        await WriteAuditAsync(actorId, AuditEventType.BackupRun, "backup", info.Id.ToString(), null, context, ct);
        await db.SaveChangesAsync(ct);
        return AppResult<AdminBackupDto>.Ok(new AdminBackupDto(
            info.Id, info.Id.ToString("N"), info.Kind, info.CreatedAt, info.State, info.VerifiedAt, info.ObjectCount, info.TotalBytes));
    }

    public async Task<AppResult> RecordConfigChangeAsync(
        Guid actorId,
        string key,
        string value,
        RequestContext context,
        CancellationToken ct = default)
    {
        if (!await IsAdminAsync(actorId, ct))
        {
            return NotFound();
        }

        await WriteAuditAsync(actorId, AuditEventType.ConfigChange, "config", key, null, context, ct,
            $"{{\"value\":{System.Text.Json.JsonSerializer.Serialize(value)}}}");
        await db.SaveChangesAsync(ct);
        return AppResult.Ok();
    }

    public async Task<AppResult> PurgeFileAsync(Guid actorId, Guid fileId, RequestContext context, CancellationToken ct = default)
    {
        if (!await IsAdminAsync(actorId, ct))
        {
            return NotFound();
        }

        var file = await db.Files.FirstOrDefaultAsync(f => f.Id == fileId, ct);
        if (file is null)
        {
            return NotFound();
        }

        // Commit-then-bytes: remove the row first, then delete store object (missing key = success).
        var storageKey = file.StorageKey;
        var purgedFileId = file.Id;
        await ShareLinkRevoker.RevokeAsync(
            db, clock, l => l.FileId == purgedFileId, ShareLinkRevoker.ReasonPurge, actorId, context, ct);
        db.Files.Remove(file);
        await WriteAuditAsync(actorId, AuditEventType.FilePurge, "file", fileId.ToString(), null, context, ct);
        await db.SaveChangesAsync(ct);

        try
        {
            await store.DeleteAsync(storageKey, ct);
        }
        catch
        {
            // Row already committed; reconcile reports missing-object / orphan store separately.
        }

        return AppResult.Ok();
    }

    public async Task<AppResult> RecordDownloadAsync(Guid actorId, Guid fileId, RequestContext context, CancellationToken ct = default)
    {
        if (!await IsAdminAsync(actorId, ct))
        {
            return NotFound();
        }

        await WriteAuditAsync(actorId, AuditEventType.FileDownload, "file", fileId.ToString(), null, context, ct);
        await db.SaveChangesAsync(ct);
        return AppResult.Ok();
    }

    public async Task<AppResult> RecordBackupRestoreAsync(Guid actorId, Guid backupId, RequestContext context, CancellationToken ct = default)
    {
        if (!await IsAdminAsync(actorId, ct))
        {
            return NotFound();
        }

        if (backupOptions.Value.RestoreViaAdmin)
        {
            await backups.RestoreAsync(backupId, ct);
        }

        await WriteAuditAsync(actorId, AuditEventType.BackupRestore, "backup", backupId.ToString(), null, context, ct);
        await db.SaveChangesAsync(ct);
        return AppResult.Ok();
    }

    private async Task<bool> IsAdminAsync(Guid actorId, CancellationToken ct)
    {
        return await db.Users.AsNoTracking().AnyAsync(u => u.Id == actorId && u.IsPlatformAdmin && u.Status == "active", ct);
    }

    private async Task WriteAuditAsync(
        Guid? actorId,
        AuditEventType type,
        string targetType,
        string targetId,
        Guid? teamId,
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
            TeamId = teamId,
            TargetType = targetType,
            TargetId = targetId,
            DetailsJson = detailsJson,
            Ip = Truncate(context.Ip, 64),
            UserAgent = Truncate(context.UserAgent, 512),
        });
        await Task.CompletedTask;
    }

    private static AdminUserDto ToUserDto(AppUser u) =>
        new(u.Id, u.Email ?? "", u.DisplayName, u.Status, u.IsPlatformAdmin, u.PersonalQuotaBytes, u.PersonalUsageBytes, u.LastLoginAt);

    private static AuditEventDto ToAuditDto(AuditEvent a) =>
        new(a.Id, a.OccurredAt, a.ActorId, a.EventType, a.TeamId, a.TargetType, a.TargetId, a.DetailsJson, a.Ip, a.UserAgent);

    private static AppResult NotFound() => AppResult.Fail(ErrorCodes.NotFound, "Not found.");

    private static AppResult<T> NotFound<T>() => AppResult<T>.Fail(ErrorCodes.NotFound, "Not found.");

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    private static string EscapeCsv(string value) =>
        "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private static string EncodeCursor(DateTimeOffset occurredAt, Guid id) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{occurredAt.UtcTicks}:{id:N}"));

    private static bool TryDecodeCursor(string cursor, out DateTimeOffset occurredAt, out Guid id)
    {
        occurredAt = default;
        id = default;
        try
        {
            var raw = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            var parts = raw.Split(':', 2);
            if (parts.Length != 2
                || !long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks)
                || !Guid.TryParseExact(parts[1], "N", out id))
            {
                return false;
            }

            occurredAt = new DateTimeOffset(ticks, TimeSpan.Zero);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
