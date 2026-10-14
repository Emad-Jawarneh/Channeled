using Channeled.Application.Abstractions.Results;
using Channeled.Application.Notifications;
using Channeled.Application.Teams;
using Channeled.Domain.Errors;
using Channeled.Domain.Identity;
using Channeled.Domain.Notifications;
using Channeled.Domain.Teams;
using Channeled.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Channeled.Infrastructure.Notifications;

public sealed class NotificationService(ApplicationDbContext db) : INotificationService
{
    public async Task<AppResult<NotificationListResponse>> ListAsync(
        Guid userId,
        int limit,
        bool unreadOnly,
        CancellationToken ct = default)
    {
        await EnsureJoinNotificationsForAdminAsync(userId, ct);

        limit = Math.Clamp(limit <= 0 ? 50 : limit, 1, 100);
        var query = db.Notifications.AsNoTracking().Where(n => n.UserId == userId);
        if (unreadOnly)
        {
            query = query.Where(n => n.ReadAt == null);
        }

        var rows = await query
            .OrderByDescending(n => n.CreatedAt)
            .ThenByDescending(n => n.Id)
            .Take(limit)
            .ToListAsync(ct);

        var unread = await db.Notifications.AsNoTracking()
            .CountAsync(n => n.UserId == userId && n.ReadAt == null, ct);

        var teamIds = rows.Select(r => r.TeamId).Distinct().ToList();
        var channelIds = rows.Select(r => r.ChannelId).Distinct().ToList();
        var actorIds = rows.Select(r => r.ActorId).Distinct().ToList();

        var teams = await db.Teams.AsNoTracking()
            .Where(t => teamIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Slug, ct);
        var channels = await db.Channels.AsNoTracking()
            .Where(c => channelIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var actors = await db.Users.AsNoTracking()
            .Where(u => actorIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

        var items = rows.Select(n => new NotificationDto(
            n.Id,
            n.Type,
            n.TeamId,
            teams.GetValueOrDefault(n.TeamId, ""),
            n.ChannelId,
            channels.GetValueOrDefault(n.ChannelId, ""),
            n.MessageId,
            n.ActorId,
            actors.GetValueOrDefault(n.ActorId, "Unknown user"),
            n.BodyPreview,
            n.CreatedAt,
            n.ReadAt)).ToList();

        return AppResult<NotificationListResponse>.Ok(new NotificationListResponse(items, unread));
    }

    public async Task<AppResult<UnreadCountResponse>> UnreadCountAsync(Guid userId, CancellationToken ct = default)
    {
        await EnsureJoinNotificationsForAdminAsync(userId, ct);

        var count = await db.Notifications.AsNoTracking()
            .CountAsync(n => n.UserId == userId && n.ReadAt == null, ct);
        return AppResult<UnreadCountResponse>.Ok(new UnreadCountResponse(count));
    }

    /// <summary>
    /// Heals coalesced legacy join notifications: one unread row per pending request for this admin.
    /// </summary>
    private async Task EnsureJoinNotificationsForAdminAsync(Guid userId, CancellationToken ct)
    {
        var adminTeamIds = await db.TeamMembers.AsNoTracking()
            .Where(m => m.UserId == userId && m.Role >= MemberRole.Admin)
            .Select(m => m.TeamId)
            .ToListAsync(ct);
        if (adminTeamIds.Count == 0)
        {
            return;
        }

        var pending = await db.TeamJoinRequests.AsNoTracking()
            .Where(r => adminTeamIds.Contains(r.TeamId) && r.Status == TeamJoinRequestStatus.Pending)
            .Select(r => new { r.Id, r.TeamId, r.UserId })
            .ToListAsync(ct);
        var pendingIds = pending.Select(p => p.Id).ToHashSet();

        await db.Notifications
            .Where(n => n.UserId == userId
                        && n.Type == NotificationType.TeamJoinRequest
                        && adminTeamIds.Contains(n.TeamId)
                        && !pendingIds.Contains(n.MessageId))
            .ExecuteDeleteAsync(ct);

        if (pending.Count == 0)
        {
            return;
        }

        var have = (await db.Notifications.AsNoTracking()
                .Where(n => n.UserId == userId
                            && n.Type == NotificationType.TeamJoinRequest
                            && adminTeamIds.Contains(n.TeamId))
                .Select(n => n.MessageId)
                .ToListAsync(ct))
            .ToHashSet();

        var missing = pending.Where(p => !have.Contains(p.Id)).ToList();
        if (missing.Count == 0)
        {
            return;
        }

        var teamNames = await db.Teams.AsNoTracking()
            .Where(t => missing.Select(m => m.TeamId).Distinct().Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Name, ct);
        var actorNames = await db.Users.AsNoTracking()
            .Where(u => missing.Select(m => m.UserId).Distinct().Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        var now = DateTimeOffset.UtcNow;

        foreach (var req in missing)
        {
            var actor = actorNames.GetValueOrDefault(req.UserId, "Someone");
            var team = teamNames.GetValueOrDefault(req.TeamId, "the team");
            var preview = $"{actor} requested to join {team}";
            db.Notifications.Add(new NotificationEntity
            {
                Id = UuidV7.NewGuid(),
                UserId = userId,
                Type = NotificationType.TeamJoinRequest,
                TeamId = req.TeamId,
                ChannelId = Guid.Empty,
                MessageId = req.Id,
                ActorId = req.UserId,
                BodyPreview = preview.Length <= 280 ? preview : preview[..280],
                CreatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<AppResult<PendingJoinCountDto>> GetPendingTeamJoinCountAsync(Guid userId, CancellationToken ct = default)
    {
        var count = await db.TeamJoinRequests.AsNoTracking()
            .CountAsync(
                r => r.Status == TeamJoinRequestStatus.Pending
                     && db.TeamMembers.Any(m => m.TeamId == r.TeamId && m.UserId == userId && m.Role >= MemberRole.Admin),
                ct);
        return AppResult<PendingJoinCountDto>.Ok(new PendingJoinCountDto(count));
    }

    public async Task<AppResult> MarkReadAsync(Guid userId, Guid notificationId, CancellationToken ct = default)
    {
        var row = await db.Notifications.FirstOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId, ct);
        if (row is null)
        {
            return AppResult.Fail(ErrorCodes.NotFound, "Notification not found.");
        }

        if (row.ReadAt is null)
        {
            row.ReadAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        return AppResult.Ok();
    }

    public async Task<AppResult> MarkAllReadAsync(Guid userId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        await db.Notifications
            .Where(n => n.UserId == userId && n.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now), ct);
        return AppResult.Ok();
    }
}
