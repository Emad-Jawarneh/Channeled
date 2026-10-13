using Channeled.Application.Abstractions.Results;
using Channeled.Application.Notifications;
using Channeled.Domain.Errors;
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
        var count = await db.Notifications.AsNoTracking()
            .CountAsync(n => n.UserId == userId && n.ReadAt == null, ct);
        return AppResult<UnreadCountResponse>.Ok(new UnreadCountResponse(count));
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
