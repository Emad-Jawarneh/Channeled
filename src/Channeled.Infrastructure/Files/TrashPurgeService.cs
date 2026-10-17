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

    /// <summary>Purge a specific batch (tests / crash-retry). Idempotent.</summary>
    Task<bool> PurgeBatchAsync(Guid batchId, CancellationToken ct = default);
}

/// <summary>
/// Nightly trash purge by <see cref="TrashBatchEntity"/>: commit rows + outbox first, then bytes.
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
        var batchIds = await db.TrashBatches.AsNoTracking()
            .Where(b => b.DeletedAt < cutoff)
            .OrderBy(b => b.DeletedAt)
            .ThenBy(b => b.Id)
            .Select(b => b.Id)
            .Take(20)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var purged = 0;
        foreach (var id in batchIds)
        {
            if (await PurgeBatchAsync(id, ct).ConfigureAwait(false))
            {
                purged++;
            }
        }

        return purged;
    }

    public async Task<bool> PurgeBatchAsync(Guid batchId, CancellationToken ct = default)
    {
        var chunk = Math.Clamp(deleteOptions.Value.ObjectDeleteChunkSize, 1, 2_000);
        var batch = await db.TrashBatches.FirstOrDefaultAsync(b => b.Id == batchId, ct).ConfigureAwait(false);
        if (batch is null)
        {
            return false;
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

        batch.State = "purging";
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        // Files first (object keys), then folders deepest-first.
        var files = await db.Files.AsNoTracking()
            .Where(f => f.DeletedBatchId == batchId)
            .Select(f => new { f.Id, f.StorageKey, f.SizeBytes, f.ChannelId, f.OwnerUserId })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var ids = files.Select(f => f.Id).ToList();
        var keys = files.Select(f => f.StorageKey).Where(k => !string.IsNullOrWhiteSpace(k)).ToList();
        var teamBytes = files
            .Where(f => f.ChannelId is not null)
            .GroupBy(f => f.ChannelId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.SizeBytes));
        var personalBytes = files
            .Where(f => f.OwnerUserId is not null)
            .GroupBy(f => f.OwnerUserId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.SizeBytes));

        if (ids.Count > 0)
        {
            await db.FileStars.Where(s => ids.Contains(s.FileId)).ExecuteDeleteAsync(ct).ConfigureAwait(false);
            await db.Files.Where(f => ids.Contains(f.Id)).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        }

        // Children before parents: repeatedly delete leaf folders in the batch.
        while (true)
        {
            var leafIds = await db.Folders.AsNoTracking()
                .Where(f => f.DeletedBatchId == batchId
                    && !db.Folders.Any(c => c.ParentId == f.Id && c.DeletedBatchId == batchId)
                    && !db.Files.Any(file => file.FolderId == f.Id && file.DeletedBatchId == batchId))
                .Select(f => f.Id)
                .Take(chunk)
                .ToListAsync(ct)
                .ConfigureAwait(false);
            if (leafIds.Count == 0)
            {
                // Force-delete any remaining (orphan structure).
                var remaining = await db.Folders.Where(f => f.DeletedBatchId == batchId)
                    .ExecuteDeleteAsync(ct)
                    .ConfigureAwait(false);
                if (remaining == 0)
                {
                    break;
                }

                continue;
            }

            await db.Folders.Where(f => leafIds.Contains(f.Id)).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        }

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
            TargetType = "trash_batch",
            TargetId = batchId.ToString(),
            DetailsJson = $"{{\"purgedFiles\":{ids.Count},\"batchId\":\"{batchId}\"}}",
            Ip = "",
            UserAgent = "",
        });

        await db.TrashBatches.Where(b => b.Id == batchId).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);

        logger.LogInformation("Trash purge batch {BatchId} removed {Count} file rows", batchId, ids.Count);
        return true;
    }
}
