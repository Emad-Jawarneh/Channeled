using Channeled.Application.Abstractions.Operations;
using Channeled.Application.Abstractions.Storage;
using Channeled.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Channeled.Infrastructure.Operations;

public sealed class SystemHealthService(
    ApplicationDbContext db,
    IFileStore store,
    IOptions<FileStoreOptions> storeOptions,
    IOptions<BackupOptions> backupOptions) : ISystemHealthService
{
    public async Task<SystemHealthReport> CheckAsync(CancellationToken cancellationToken = default)
    {
        var dbOk = false;
        string? detail = null;
        try
        {
            dbOk = await db.Database.CanConnectAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            detail = "database: " + ex.Message;
        }

        var storeOk = true;
        try
        {
            _ = await store.ExistsAsync("healthcheck-missing-object", cancellationToken);
        }
        catch (Exception ex)
        {
            storeOk = false;
            detail = (detail is null ? "" : detail + "; ") + "store: " + ex.Message;
        }

        long free = 0;
        var diskOk = true;
        try
        {
            var root = Path.GetFullPath(storeOptions.Value.RootPath);
            Directory.CreateDirectory(root);
            free = new DriveInfo(Path.GetPathRoot(root) ?? root).AvailableFreeSpace;
            diskOk = free >= backupOptions.Value.LowDiskBytes;
        }
        catch (Exception ex)
        {
            diskOk = false;
            detail = (detail is null ? "" : detail + "; ") + "disk: " + ex.Message;
        }

        var pending = 0;
        var dead = 0;
        if (dbOk)
        {
            try
            {
                pending = await db.Jobs.CountAsync(j => j.State == "pending" || j.State == "running", cancellationToken);
                dead = await db.Jobs.CountAsync(j => j.State == "dead", cancellationToken);
            }
            catch (Exception ex)
            {
                dbOk = false;
                detail = (detail is null ? "" : detail + "; ") + "jobs: " + ex.Message;
            }
        }

        var status = dbOk && storeOk && diskOk && dead == 0 ? "ok" : "degraded";
        if (dead > 0)
        {
            detail = (detail is null ? "" : detail + "; ") + $"dead_jobs:{dead}";
        }

        return new SystemHealthReport(
            status,
            Api: true,
            dbOk,
            storeOk,
            diskOk,
            free,
            pending,
            dead,
            storeOptions.Value.Kind.ToString(),
            detail);
    }
}