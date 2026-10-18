using Channeled.Domain.Identity;
using Channeled.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Channeled.Infrastructure.Files;

/// <summary>Set-based trash/restore by <see cref="TrashBatchEntity"/>.</summary>
public static class TrashCascade
{
    public static async Task<(TrashBatchEntity Batch, int ItemCount, List<Guid> NestedFileIds)> TrashFolderTreeAsync(
        ApplicationDbContext db,
        Guid rootFolderId,
        Guid? channelId,
        Guid? ownerUserId,
        DateTimeOffset now,
        int maxItems,
        CancellationToken ct)
    {
        var folderIds = await CollectDescendantFolderIdsAsync(db, rootFolderId, ct);
        var fileQuery = db.Files.AsNoTracking().Where(f => f.DeletedAt == null);
        if (channelId is Guid cid)
        {
            fileQuery = fileQuery.Where(f => f.ChannelId == cid && f.FolderId != null && folderIds.Contains(f.FolderId.Value));
        }
        else if (ownerUserId is Guid oid)
        {
            fileQuery = fileQuery.Where(f => f.OwnerUserId == oid && f.FolderId != null && folderIds.Contains(f.FolderId.Value));
        }

        var nestedFileIds = await fileQuery.Select(f => f.Id).ToListAsync(ct);
        var liveFolderCount = await db.Folders.CountAsync(
            f => folderIds.Contains(f.Id) && f.DeletedAt == null,
            ct);
        var itemCount = liveFolderCount + nestedFileIds.Count;
        if (itemCount > maxItems)
        {
            throw new TrashCapExceededException(itemCount, maxItems);
        }

        var batch = new TrashBatchEntity
        {
            Id = UuidV7.NewGuid(),
            DeletedAt = now,
            Scope = channelId is not null ? "channel" : "personal",
            ChannelId = channelId,
            OwnerUserId = ownerUserId,
            ItemCount = itemCount,
            State = "none",
        };
        db.TrashBatches.Add(batch);
        await db.SaveChangesAsync(ct);

        if (channelId is Guid channel)
        {
            await db.Folders
                .Where(f => folderIds.Contains(f.Id) && f.ChannelId == channel && f.DeletedAt == null)
                .ExecuteUpdateAsync(
                    s => s
                        .SetProperty(f => f.DeletedAt, now)
                        .SetProperty(f => f.UpdatedAt, now)
                        .SetProperty(f => f.DeletedBatchId, batch.Id),
                    ct);
            if (nestedFileIds.Count > 0)
            {
                await db.Files
                    .Where(f => nestedFileIds.Contains(f.Id))
                    .ExecuteUpdateAsync(
                        s => s
                            .SetProperty(f => f.DeletedAt, now)
                            .SetProperty(f => f.UpdatedAt, now)
                            .SetProperty(f => f.DeletedBatchId, batch.Id),
                        ct);
            }
        }
        else if (ownerUserId is Guid owner)
        {
            await db.Folders
                .Where(f => folderIds.Contains(f.Id) && f.OwnerUserId == owner && f.DeletedAt == null)
                .ExecuteUpdateAsync(
                    s => s
                        .SetProperty(f => f.DeletedAt, now)
                        .SetProperty(f => f.UpdatedAt, now)
                        .SetProperty(f => f.DeletedBatchId, batch.Id),
                    ct);
            if (nestedFileIds.Count > 0)
            {
                await db.Files
                    .Where(f => nestedFileIds.Contains(f.Id))
                    .ExecuteUpdateAsync(
                        s => s
                            .SetProperty(f => f.DeletedAt, now)
                            .SetProperty(f => f.UpdatedAt, now)
                            .SetProperty(f => f.DeletedBatchId, batch.Id),
                        ct);
            }
        }

        return (batch, itemCount, nestedFileIds);
    }

    public static async Task TrashSingleFileAsync(
        ApplicationDbContext db,
        FileEntity file,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var batch = new TrashBatchEntity
        {
            Id = UuidV7.NewGuid(),
            DeletedAt = now,
            Scope = file.ChannelId is not null ? "channel" : "personal",
            ChannelId = file.ChannelId,
            OwnerUserId = file.OwnerUserId,
            ItemCount = 1,
            State = "none",
        };
        db.TrashBatches.Add(batch);
        await db.SaveChangesAsync(ct);
        file.DeletedAt = now;
        file.UpdatedAt = now;
        file.DeletedBatchId = batch.Id;
    }

    public static async Task<List<Guid>> CollectDescendantFolderIdsAsync(
        ApplicationDbContext db,
        Guid rootFolderId,
        CancellationToken ct)
    {
        var all = new List<Guid> { rootFolderId };
        var frontier = new List<Guid> { rootFolderId };
        while (frontier.Count > 0)
        {
            var children = await db.Folders.AsNoTracking()
                .Where(f => f.ParentId != null && frontier.Contains(f.ParentId.Value))
                .Select(f => f.Id)
                .ToListAsync(ct);
            frontier = children.Where(id => !all.Contains(id)).ToList();
            all.AddRange(frontier);
        }

        return all;
    }

    public static async Task<bool> HasTrashedAncestorAsync(
        ApplicationDbContext db,
        Guid? folderId,
        CancellationToken ct)
    {
        var current = folderId;
        while (current is Guid id)
        {
            var row = await db.Folders.AsNoTracking()
                .Where(f => f.Id == id)
                .Select(f => new { f.ParentId, f.DeletedAt })
                .FirstOrDefaultAsync(ct);
            if (row is null)
            {
                return true;
            }

            if (row.DeletedAt is not null)
            {
                return true;
            }

            current = row.ParentId;
        }

        return false;
    }

    public static string AllocateUniqueName(string desired, HashSet<string> takenLower)
    {
        if (!takenLower.Contains(desired.ToLowerInvariant()))
        {
            return desired;
        }

        var baseName = desired;
        var ext = "";
        var dot = desired.LastIndexOf('.');
        if (dot > 0)
        {
            baseName = desired[..dot];
            ext = desired[dot..];
        }

        for (var i = 1; i < 10_000; i++)
        {
            var candidate = i == 1 ? $"{baseName} (copy){ext}" : $"{baseName} (copy {i}){ext}";
            if (!takenLower.Contains(candidate.ToLowerInvariant()))
            {
                return candidate;
            }
        }

        return $"{baseName} (copy {Guid.NewGuid():N}){ext}";
    }
}

public sealed class TrashCapExceededException(int count, int max) : Exception(
    $"Trash/restore would touch {count:N0} items (max {max:N0}).")
{
    public int Count { get; } = count;
    public int Max { get; } = max;
}
