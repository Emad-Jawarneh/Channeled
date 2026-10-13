using Channeled.Application.Abstractions.Storage;
using Channeled.Application.Abstractions.Time;
using Channeled.Application.Teams;
using Channeled.Domain.Audit;
using Channeled.Domain.Identity;
using Channeled.Infrastructure.Jobs;
using Channeled.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Channeled.Infrastructure.Files;

public interface ITrashPurgeService
{
    Task<int> PurgeExpiredAsync(CancellationToken ct = default);
}

/// <summary>
/// Nightly trash purge: commit row removal (+ outbox) first, then bytes via object_delete jobs.
/// </summary>
public sealed class TrashPurgeService(
    ApplicationDbContext db,
    IClock clock,
    IOptions<UploadLimitsOptions> uploadLimits,
    IOptions<ChannelDeleteOptions> deleteOptions,
    ILogger<TrashPurgeService> logger) : ITrashPurgeService
{
    public async Task<int> PurgeExpiredAsync(CancellationToken ct = default)
    {
        var retentionDays = Math.Max(0, uploadLimits.Value.TrashRetentionDays);
        var cutoff = clock.UtcNow.AddDays(-retentionDays);
        var chunk = Math.Clamp(deleteOptions.Value.ObjectDeleteChunkSize, 1, 2_000);

        var expired = await db.Files.AsNoTracking()
            .Where(f => f.DeletedAt != null && f.DeletedAt < cutoff)
            .OrderBy(f => f.DeletedAt)
            .Take(chunk)
            .Select(f => new { f.Id, f.StorageKey, f.SizeBytes, f.ChannelId, f.OwnerUserId })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (expired.Count == 0)
        {
            return 0;
        }

        var ids = expired.Select(f => f.Id).ToList();
        var keys = expired.Select(f => f.StorageKey).ToList();
        var teamBytes = expired
            .Where(f => f.ChannelId is not null)
            .GroupBy(f => f.ChannelId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.SizeBytes));
        var personalBytes = expired
            .Where(f => f.OwnerUserId is not null)
            .GroupBy(f => f.OwnerUserId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.SizeBytes));

        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

        await db.FileStars.Where(s => ids.Contains(s.FileId)).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        await db.Files.Where(f => ids.Contains(f.Id)).ExecuteDeleteAsync(ct).ConfigureAwait(false);

        foreach (var (channelId, bytes) in teamBytes)
        {
            var teamId = await db.Channels.AsNoTracking()
                .Where(c => c.Id == channelId)
                .Select(c => (Guid?)c.TeamId)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);
            if (teamId is Guid tid && bytes > 0)
            {
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"""UPDATE teams SET "UsageBytes" = GREATEST("UsageBytes" - {bytes}, 0) WHERE "Id" = {tid}""",
                    ct).ConfigureAwait(false);
            }
        }

        foreach (var (userId, bytes) in personalBytes)
        {
            if (bytes > 0)
            {
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"""UPDATE "AspNetUsers" SET "PersonalUsageBytes" = GREATEST("PersonalUsageBytes" - {bytes}, 0) WHERE "Id" = {userId}""",
                    ct).ConfigureAwait(false);
            }
        }

        ObjectDeleteOutbox.Enqueue(db, clock.UtcNow, keys, multiparts: null, chunk);
        db.AuditEvents.Add(new AuditEvent
        {
            Id = UuidV7.NewGuid(),
            OccurredAt = clock.UtcNow,
            ActorId = null,
            EventType = AuditEventType.FilePurge,
            TeamId = null,
            TargetType = "trash_purge",
            TargetId = cutoff.ToString("O"),
            DetailsJson = $"{{\"purgedFiles\":{ids.Count}}}",
            Ip = "",
            UserAgent = "",
        });

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);

        logger.LogInformation("Trash purge committed {Count} file rows for outbox delete", ids.Count);
        return ids.Count;
    }
}
