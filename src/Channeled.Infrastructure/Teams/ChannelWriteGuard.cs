using System.Data;
using System.Data.Common;
using Channeled.Application.Abstractions.Results;
using Channeled.Domain.Errors;
using Channeled.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Channeled.Infrastructure.Teams;

/// <summary>
/// Serializes channel file/folder writers against deletes: <c>FOR SHARE</c> then exists-check.
/// Callers must already be inside a DB transaction.
/// </summary>
public sealed class ChannelWriteGuard(ApplicationDbContext db)
{
    /// <summary>Test-only hook invoked immediately before acquiring the row lock.</summary>
    public static Func<Guid, string, CancellationToken, Task>? BeforeLockHook { get; set; }

    /// <summary>Test-only hook invoked after the lock SQL returns (before exists-check fail).</summary>
    public static Func<Guid, string, CancellationToken, Task>? AfterLockHook { get; set; }

    public Task<AppResult> EnsureSharedAsync(Guid channelId, CancellationToken ct = default) =>
        LockAsync(channelId, forUpdate: false, ct);

    public Task<AppResult> LockForUpdateAsync(Guid channelId, CancellationToken ct = default) =>
        LockAsync(channelId, forUpdate: true, ct);

    /// <summary>Locks channels in ascending Id order (mix of FOR UPDATE / FOR SHARE).</summary>
    public async Task<AppResult> LockOrderedAsync(
        IEnumerable<(Guid Id, bool ForUpdate)> locks,
        CancellationToken ct = default)
    {
        foreach (var (id, forUpdate) in locks.OrderBy(x => x.Id))
        {
            var result = await LockAsync(id, forUpdate, ct).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                return result;
            }
        }

        return AppResult.Ok();
    }

    private async Task<AppResult> LockAsync(Guid channelId, bool forUpdate, CancellationToken ct)
    {
        var mode = forUpdate ? "update" : "share";
        try
        {
            if (BeforeLockHook is not null)
            {
                await BeforeLockHook(channelId, mode, ct).ConfigureAwait(false);
            }

            var found = await ExecuteLockAsync(channelId, forUpdate, ct).ConfigureAwait(false);
            if (AfterLockHook is not null)
            {
                await AfterLockHook(channelId, mode, ct).ConfigureAwait(false);
            }

            if (!found)
            {
                return AppResult.Fail(ErrorCodes.NotFound, "Channel not found.");
            }

            return AppResult.Ok();
        }
        catch (PostgresException ex) when (ex.SqlState is PostgresErrorCodes.LockNotAvailable or PostgresErrorCodes.QueryCanceled)
        {
            return AppResult.Fail(ErrorCodes.LockTimeout, "Channel is busy; retry shortly.");
        }
        catch (DbException ex) when (ex.InnerException is PostgresException pg
                                      && pg.SqlState is PostgresErrorCodes.LockNotAvailable or PostgresErrorCodes.QueryCanceled)
        {
            return AppResult.Fail(ErrorCodes.LockTimeout, "Channel is busy; retry shortly.");
        }
    }

    private async Task<bool> ExecuteLockAsync(Guid channelId, bool forUpdate, CancellationToken ct)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
        {
            await db.Database.OpenConnectionAsync(ct).ConfigureAwait(false);
        }

        await using var cmd = conn.CreateCommand();
        cmd.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        cmd.CommandText = forUpdate
            ? """SELECT 1 FROM channels WHERE "Id" = @id FOR UPDATE"""
            : """SELECT 1 FROM channels WHERE "Id" = @id FOR SHARE""";
        var p = cmd.CreateParameter();
        p.ParameterName = "id";
        p.Value = channelId;
        cmd.Parameters.Add(p);

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is not null && result is not DBNull;
    }
}
