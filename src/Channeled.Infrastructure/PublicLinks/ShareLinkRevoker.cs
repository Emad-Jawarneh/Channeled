using System.Linq.Expressions;
using System.Text.Json;
using Channeled.Application.Abstractions.Time;
using Channeled.Application.Identity;
using Channeled.Domain.Audit;
using Channeled.Domain.Identity;
using Channeled.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Channeled.Infrastructure.PublicLinks;

/// <summary>
/// Marks matching active links revoked and emits a FileShareRevoke audit event per link. Changes are only
/// tracked on the DbContext: the caller's single SaveChanges commits them atomically with the triggering
/// change (trash, cross-channel move, archive, purge, ...).
/// </summary>
public static class ShareLinkRevoker
{
    public const string ReasonTrash = "file_trashed";
    public const string ReasonFolderTrash = "folder_trashed";
    public const string ReasonMove = "channel_move";
    public const string ReasonChannelArchive = "channel_archived";
    public const string ReasonTeamArchive = "team_archived";
    public const string ReasonChannelDelete = "channel_deleted";
    public const string ReasonTeamDelete = "team_deleted";
    public const string ReasonPurge = "file_purged";
    public const string ReasonChannelRestricted = "channel_restricted";
    public const string ReasonManual = "manual";
    public const string ReasonAdmin = "admin_revoke_all";

    public static async Task<int> RevokeAsync(
        ApplicationDbContext db,
        IClock clock,
        Expression<Func<FileShareLinkEntity, bool>> predicate,
        string reason,
        Guid? actorId,
        RequestContext? context,
        CancellationToken ct)
    {
        var links = await db.FileShareLinks
            .Where(l => l.RevokedAt == null)
            .Where(predicate)
            .ToListAsync(ct);
        foreach (var link in links)
        {
            Apply(db, clock, link, reason, actorId, context);
        }

        return links.Count;
    }

    public static void Apply(
        ApplicationDbContext db,
        IClock clock,
        FileShareLinkEntity link,
        string reason,
        Guid? actorId,
        RequestContext? context)
    {
        var now = clock.UtcNow;
        link.RevokedAt = now;
        link.RevokedBy = actorId;
        link.RevokeReason = reason;
        db.AuditEvents.Add(new AuditEvent
        {
            Id = UuidV7.NewGuid(),
            OccurredAt = now,
            ActorId = actorId,
            EventType = AuditEventType.FileShareRevoke,
            TeamId = link.TeamId,
            TargetType = "file_share_link",
            TargetId = link.Id.ToString(),
            DetailsJson = JsonSerializer.Serialize(new { reason, fileId = link.FileId, channelId = link.ChannelId }),
            Ip = Truncate(context?.Ip ?? "", 64),
            UserAgent = Truncate(context?.UserAgent ?? "", 512),
        });
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
