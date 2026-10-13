using System.Security.Cryptography;
using Channeled.Application.Abstractions.Operations;
using Channeled.Application.Abstractions.Storage;
using Channeled.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Channeled.Infrastructure.Operations;

public sealed class StoreReconciliationService(
    ApplicationDbContext db,
    IFileStore store) : IStoreReconciliationService
{
    public async Task<StoreReconcileReport> ReconcileAsync(CancellationToken cancellationToken = default)
    {
        var files = await db.Files.AsNoTracking()
            .Select(f => new { f.StorageKey, f.Sha256, f.SizeBytes })
            .ToListAsync(cancellationToken);

        var dbKeys = files.Select(f => f.StorageKey).ToHashSet(StringComparer.Ordinal);
        var storeKeys = (await store.ListObjectKeysAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);

        var missing = dbKeys.Where(k => !storeKeys.Contains(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();
        var orphans = storeKeys.Where(k => !dbKeys.Contains(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();

        var mismatches = new List<string>();
        foreach (var file in files)
        {
            if (missing.Contains(file.StorageKey) || string.IsNullOrWhiteSpace(file.Sha256))
            {
                continue;
            }

            await using var stream = await store.OpenReadAsync(file.StorageKey, cancellationToken: cancellationToken);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
            if (!string.Equals(hash, file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                mismatches.Add(file.StorageKey);
            }
        }

        var filesWithoutChannel = await db.Files.AsNoTracking()
            .Where(f => f.ChannelId == null && f.OwnerUserId == null)
            .Select(f => f.Id)
            .OrderBy(id => id)
            .ToListAsync(cancellationToken);

        return new StoreReconcileReport(
            dbKeys.Count,
            storeKeys.Count,
            missing,
            orphans,
            mismatches,
            filesWithoutChannel);
    }
}
