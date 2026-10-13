using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Channeled.Application.Abstractions.Operations;
using Channeled.Application.Abstractions.Storage;
using Channeled.Application.Abstractions.Time;
using Channeled.Domain.Identity;
using Channeled.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Channeled.Infrastructure.Operations;

public sealed class BackupService(
    ApplicationDbContext db,
    IFileStore store,
    IOptions<FileStoreOptions> storeOptions,
    IOptions<BackupOptions> backupOptions,
    IConfiguration configuration,
    IClock clock,
    IMaintenanceMode maintenance,
    IStoreReconciliationService reconcile,
    IOperatorAlertService alerts,
    ILogger<BackupService> logger) : IBackupService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task<BackupPackageInfo> CreateAsync(string kind = "ondemand", CancellationToken cancellationToken = default)
    {
        var options = backupOptions.Value;
        Directory.CreateDirectory(options.RootPath);

        var id = UuidV7.NewGuid();
        var dir = Path.Combine(Path.GetFullPath(options.RootPath), id.ToString("N"));
        Directory.CreateDirectory(dir);

        var entity = new BackupEntity
        {
            Id = id,
            Directory = dir,
            Kind = NormalizeKind(kind),
            State = "running",
            CreatedAt = clock.UtcNow,
        };
        db.Backups.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            var dumpPath = Path.Combine(dir, "database.dump");
            await RunPgDumpAsync(dumpPath, cancellationToken);

            var files = await db.Files.AsNoTracking()
                .Select(f => new { f.StorageKey, f.Sha256, f.SizeBytes, f.ContentType })
                .ToListAsync(cancellationToken);

            var objectsDir = Path.Combine(dir, "objects");
            Directory.CreateDirectory(objectsDir);
            var manifest = new List<BackupManifestEntry>(files.Count);
            long totalBytes = 0;

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var dest = ObjectBackupPath(objectsDir, file.StorageKey);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

                await using (var src = await store.OpenReadAsync(file.StorageKey, cancellationToken: cancellationToken))
                await using (var dst = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
                {
                    await src.CopyToAsync(dst, cancellationToken);
                }

                var sha = file.Sha256;
                if (string.IsNullOrWhiteSpace(sha))
                {
                    await using var hashStream = File.OpenRead(dest);
                    sha = Convert.ToHexString(await SHA256.HashDataAsync(hashStream, cancellationToken)).ToLowerInvariant();
                }

                var size = new FileInfo(dest).Length;
                totalBytes += size;
                manifest.Add(new BackupManifestEntry(file.StorageKey, size, sha.ToLowerInvariant()));
            }

            var manifestPath = Path.Combine(dir, "manifest.json");
            await File.WriteAllTextAsync(
                manifestPath,
                JsonSerializer.Serialize(manifest, JsonOptions),
                cancellationToken);

            await VerifyPackageAsync(dir, manifest, cancellationToken);

            entity.State = "verified";
            entity.VerifiedAt = clock.UtcNow;
            entity.ObjectCount = manifest.Count;
            entity.TotalBytes = totalBytes;
            await db.SaveChangesAsync(cancellationToken);

            await ApplyRetentionAsync(cancellationToken);

            logger.LogInformation("Backup {BackupId} verified ({Objects} objects, {Bytes} bytes)", id, manifest.Count, totalBytes);
            return ToInfo(entity);
        }
        catch (Exception ex)
        {
            entity.State = "failed";
            entity.Error = Truncate(ex.Message, 2000);
            await db.SaveChangesAsync(CancellationToken.None);
            logger.LogError(ex, "Backup {BackupId} failed", id);
            await alerts.AlertAsync("Backup failed", $"Backup {id} failed: {ex.Message}", CancellationToken.None);
            throw;
        }
    }

    public async Task<BackupPackageInfo> RestoreAsync(Guid backupId, CancellationToken cancellationToken = default)
    {
        var entity = await db.Backups.FirstOrDefaultAsync(b => b.Id == backupId, cancellationToken)
            ?? throw new InvalidOperationException($"Backup '{backupId}' was not found.");

        if (!Directory.Exists(entity.Directory))
        {
            throw new InvalidOperationException($"Backup directory '{entity.Directory}' is missing.");
        }

        var dumpPath = Path.Combine(entity.Directory, "database.dump");
        var manifestPath = Path.Combine(entity.Directory, "manifest.json");
        if (!File.Exists(dumpPath) || !File.Exists(manifestPath))
        {
            throw new InvalidOperationException("Backup package is incomplete (dump or manifest missing).");
        }

        var manifest = JsonSerializer.Deserialize<List<BackupManifestEntry>>(
            await File.ReadAllTextAsync(manifestPath, cancellationToken),
            JsonOptions) ?? [];

        maintenance.SetEnabled(true);
        try
        {
            var packageDir = entity.Directory;
            await RunPgRestoreAsync(dumpPath, cancellationToken);
            await RevokeAllPublicLinksAfterRestoreAsync(cancellationToken);
            await ClearStoreAsync(cancellationToken);

            var objectsDir = Path.Combine(packageDir, "objects");
            foreach (var entry in manifest)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var src = ObjectBackupPath(objectsDir, entry.ObjectKey);
                await using var stream = File.OpenRead(src);
                await store.PutAsync(entry.ObjectKey, stream, "application/octet-stream", cancellationToken);
            }

            db.ChangeTracker.Clear();

            var report = await reconcile.ReconcileAsync(cancellationToken);
            if (!report.IsClean)
            {
                throw new InvalidOperationException(
                    $"Restore reconciliation failed: missing={report.MissingInStore.Count}, orphans={report.OrphansInStore.Count}, checksum={report.ChecksumMismatches.Count}");
            }

            var restored = await db.Backups.FirstOrDefaultAsync(b => b.Id == backupId, cancellationToken);
            if (restored is not null)
            {
                restored.State = "restored";
                await db.SaveChangesAsync(cancellationToken);
                logger.LogInformation("Backup {BackupId} restored with clean reconciliation", backupId);
                return ToInfo(restored);
            }

            logger.LogInformation("Backup {BackupId} restored with clean reconciliation (row missing post-restore)", backupId);
            return new BackupPackageInfo(backupId, packageDir, entity.Kind, "restored", entity.CreatedAt, entity.VerifiedAt, entity.ObjectCount, entity.TotalBytes, null);
        }
        finally
        {
            maintenance.SetEnabled(false);
        }
    }

    /// <summary>
    /// A restored database may resurrect links that were revoked after the backup was taken, so every
    /// public link is revoked on restore. Best-effort: the table may not exist in older dumps.
    /// </summary>
    private async Task RevokeAllPublicLinksAfterRestoreAsync(CancellationToken ct)
    {
        try
        {
            var revoked = await db.Database.ExecuteSqlRawAsync(
                "UPDATE file_share_links SET revoked_at = now(), revoke_reason = 'backup_restore' WHERE revoked_at IS NULL",
                ct);
            logger.LogInformation("Revoked {Count} public share links after restore", revoked);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not revoke public share links after restore (table missing in dump?)");
        }
    }

    public async Task<IReadOnlyList<BackupPackageInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        var rows = await db.Backups.AsNoTracking()
            .OrderByDescending(b => b.CreatedAt)
            .Take(100)
            .ToListAsync(cancellationToken);
        return rows.Select(ToInfo).ToList();
    }

    public async Task<BackupPackageInfo?> GetAsync(Guid backupId, CancellationToken cancellationToken = default)
    {
        var row = await db.Backups.AsNoTracking().FirstOrDefaultAsync(b => b.Id == backupId, cancellationToken);
        return row is null ? null : ToInfo(row);
    }

    private async Task VerifyPackageAsync(string dir, IReadOnlyList<BackupManifestEntry> manifest, CancellationToken ct)
    {
        if (!File.Exists(Path.Combine(dir, "database.dump")))
        {
            throw new InvalidOperationException("Post-backup verification failed: database.dump missing.");
        }

        var objectsDir = Path.Combine(dir, "objects");
        foreach (var entry in manifest)
        {
            var path = ObjectBackupPath(objectsDir, entry.ObjectKey);
            if (!File.Exists(path))
            {
                throw new InvalidOperationException($"Post-backup verification failed: missing object '{entry.ObjectKey}'.");
            }

            var size = new FileInfo(path).Length;
            if (size != entry.SizeBytes)
            {
                throw new InvalidOperationException($"Post-backup verification failed: size mismatch for '{entry.ObjectKey}'.");
            }

            await using var stream = File.OpenRead(path);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant();
            if (!string.Equals(hash, entry.Sha256Hex, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Post-backup verification failed: checksum mismatch for '{entry.ObjectKey}'.");
            }
        }
    }

    private async Task ApplyRetentionAsync(CancellationToken ct)
    {
        var options = backupOptions.Value;
        var all = await db.Backups
            .Where(b => b.State == "verified" || b.State == "restored")
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync(ct);

        var keep = new HashSet<Guid>();
        KeepNewest(all.Where(b => b.Kind is "daily" or "ondemand"), options.RetainDaily, keep);
        KeepNewest(all.Where(b => b.Kind == "weekly"), options.RetainWeekly, keep);
        KeepNewest(all.Where(b => b.Kind == "monthly"), options.RetainMonthly, keep);

        // Always keep latest verified regardless of kind
        var latest = all.FirstOrDefault();
        if (latest is not null)
        {
            keep.Add(latest.Id);
        }

        foreach (var doomed in all.Where(b => !keep.Contains(b.Id)))
        {
            TryDeleteDirectory(doomed.Directory);
            db.Backups.Remove(doomed);
        }

        await db.SaveChangesAsync(ct);
    }

    private static void KeepNewest(IEnumerable<BackupEntity> items, int count, HashSet<Guid> keep)
    {
        foreach (var item in items.Take(Math.Max(0, count)))
        {
            keep.Add(item.Id);
        }
    }

    private async Task ClearStoreAsync(CancellationToken ct)
    {
        var keys = await store.ListObjectKeysAsync(ct);
        foreach (var key in keys)
        {
            await store.DeleteAsync(key, ct);
        }

        // Filesystem: also wipe leftover sidecars under root
        if (storeOptions.Value.Kind == FileStoreKind.FileSystem)
        {
            var root = Path.GetFullPath(storeOptions.Value.RootPath);
            if (Directory.Exists(root))
            {
                foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        File.Delete(path);
                    }
                    catch
                    {
                        // best-effort
                    }
                }
            }
        }
    }

    private async Task RunPgDumpAsync(string dumpPath, CancellationToken ct)
    {
        var cs = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Connection string 'Postgres' is required.");
        var b = new NpgsqlConnectionStringBuilder(cs);
        var psi = new ProcessStartInfo
        {
            FileName = backupOptions.Value.PgDumpPath,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("--format=custom");
        psi.ArgumentList.Add("--file=" + dumpPath);
        psi.ArgumentList.Add("--no-owner");
        psi.ArgumentList.Add("--no-acl");
        psi.ArgumentList.Add("--dbname=" + b.Database);
        psi.ArgumentList.Add("--host=" + b.Host);
        psi.ArgumentList.Add("--port=" + b.Port.ToString(CultureInfo.InvariantCulture));
        psi.ArgumentList.Add("--username=" + b.Username);
        psi.Environment["PGPASSWORD"] = b.Password;

        await RunProcessAsync(psi, "pg_dump", ct);
    }

    private async Task RunPgRestoreAsync(string dumpPath, CancellationToken ct)
    {
        var cs = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Connection string 'Postgres' is required.");
        var b = new NpgsqlConnectionStringBuilder(cs);

        await using (var admin = new NpgsqlConnection(cs))
        {
            await admin.OpenAsync(ct);
            await using var cmd = admin.CreateCommand();
            cmd.CommandText = """
                SELECT pg_terminate_backend(pid)
                FROM pg_stat_activity
                WHERE datname = current_database()
                  AND pid <> pg_backend_pid()
                  AND backend_type = 'client backend';
                """;
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await db.Database.CloseConnectionAsync();
        NpgsqlConnection.ClearAllPools();

        // Filter out EXTENSION entries — app role is often not owner of pg_trgm.
        var listPath = dumpPath + ".list";
        var listPsi = new ProcessStartInfo
        {
            FileName = backupOptions.Value.PgRestorePath,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        listPsi.ArgumentList.Add("--list");
        listPsi.ArgumentList.Add(dumpPath);
        listPsi.Environment["PGPASSWORD"] = b.Password;
        var listOutput = await RunProcessCaptureAsync(listPsi, "pg_restore --list", ct);
        var filtered = listOutput
            .Split('\n')
            .Where(line => !line.Contains(" EXTENSION ", StringComparison.OrdinalIgnoreCase))
            .Where(line => !line.Contains("COMMENT - EXTENSION", StringComparison.OrdinalIgnoreCase));
        await File.WriteAllTextAsync(listPath, string.Join('\n', filtered) + "\n", ct);

        var psi = new ProcessStartInfo
        {
            FileName = backupOptions.Value.PgRestorePath,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("--clean");
        psi.ArgumentList.Add("--if-exists");
        psi.ArgumentList.Add("--no-owner");
        psi.ArgumentList.Add("--no-acl");
        psi.ArgumentList.Add("--use-list=" + listPath);
        psi.ArgumentList.Add("--dbname=" + b.Database);
        psi.ArgumentList.Add("--host=" + b.Host);
        psi.ArgumentList.Add("--port=" + b.Port.ToString(CultureInfo.InvariantCulture));
        psi.ArgumentList.Add("--username=" + b.Username);
        psi.ArgumentList.Add(dumpPath);
        psi.Environment["PGPASSWORD"] = b.Password;

        await RunProcessAsync(psi, "pg_restore", ct, allowWarnings: true);
    }

    private static async Task<string> RunProcessCaptureAsync(ProcessStartInfo psi, string name, CancellationToken ct)
    {
        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start {name}.");
        var stdout = await process.StandardOutput.ReadToEndAsync(ct);
        var stderr = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{name} failed ({process.ExitCode}): {stderr}\n{stdout}");
        }

        return stdout;
    }

    private static async Task RunProcessAsync(
        ProcessStartInfo psi,
        string name,
        CancellationToken ct,
        bool allowWarnings = false)
    {
        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start {name}.");
        var stderr = await process.StandardError.ReadToEndAsync(ct);
        var stdout = await process.StandardOutput.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        if (process.ExitCode == 0)
        {
            return;
        }

        // pg_restore often exits 1 for non-fatal notices when using --clean.
        if (allowWarnings && process.ExitCode == 1 && !stderr.Contains("ERROR", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        throw new InvalidOperationException($"{name} failed ({process.ExitCode}): {stderr}\n{stdout}");
    }

    private static string ObjectBackupPath(string objectsDir, string objectKey)
    {
        var safe = objectKey.Replace('\\', '/');
        if (safe.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(safe))
        {
            throw new InvalidOperationException($"Unsafe object key '{objectKey}'.");
        }

        return Path.Combine(objectsDir, safe.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string NormalizeKind(string kind) =>
        kind.ToLowerInvariant() switch
        {
            "daily" => "daily",
            "weekly" => "weekly",
            "monthly" => "monthly",
            _ => "ondemand",
        };

    private static BackupPackageInfo ToInfo(BackupEntity e) =>
        new(e.Id, e.Directory, e.Kind, e.State, e.CreatedAt, e.VerifiedAt, e.ObjectCount, e.TotalBytes, e.Error);

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // retention best-effort
        }
    }
}
