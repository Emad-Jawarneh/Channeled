using Channeled.Application.Abstractions.Storage;
using Channeled.Application.Abstractions.Time;
using Channeled.Application.Teams;
using Channeled.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Channeled.Infrastructure.Jobs;

public interface IObjectDeleteJobProcessor
{
    /// <summary>Processes pending object_delete jobs until none remain or <paramref name="maxJobs"/> is hit.</summary>
    Task<int> ProcessPendingAsync(int maxJobs = 100, CancellationToken ct = default);
}

public sealed class ObjectDeleteJobProcessor(
    ApplicationDbContext db,
    IFileStore store,
    IClock clock,
    IOptions<ChannelDeleteOptions> options,
    ILogger<ObjectDeleteJobProcessor> logger) : IObjectDeleteJobProcessor
{
    public async Task<int> ProcessPendingAsync(int maxJobs = 100, CancellationToken ct = default)
    {
        var processed = 0;
        while (processed < maxJobs)
        {
            var job = await ClaimNextAsync(ct).ConfigureAwait(false);
            if (job is null)
            {
                break;
            }

            await RunJobAsync(job, ct).ConfigureAwait(false);
            processed++;
        }

        return processed;
    }

    private async Task<JobEntity?> ClaimNextAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

        var jobId = await db.Database
            .SqlQueryRaw<Guid>(
                """
                SELECT j."Id" AS "Value"
                FROM jobs j
                WHERE j."Type" = {0}
                  AND j."State" = 'pending'
                  AND (j."AvailableAt" IS NULL OR j."AvailableAt" <= {1})
                ORDER BY j."CreatedAt"
                FOR UPDATE SKIP LOCKED
                LIMIT 1
                """,
                ObjectDeleteJobTypes.Type,
                now)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (jobId == Guid.Empty)
        {
            await tx.CommitAsync(ct).ConfigureAwait(false);
            return null;
        }

        var job = await db.Jobs.FirstAsync(j => j.Id == jobId, ct).ConfigureAwait(false);
        job.State = "running";
        job.StartedAt = now;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
        return job;
    }

    private async Task RunJobAsync(JobEntity job, CancellationToken ct)
    {
        var payload = ObjectDeleteOutbox.Parse(job.DetailsJson);
        if (payload is null)
        {
            await MarkDeadAsync(job, "Invalid job payload.", ct).ConfigureAwait(false);
            return;
        }

        try
        {
            foreach (var multipart in payload.Multiparts)
            {
                try
                {
                    await store.AbortMultipartUploadAsync(multipart.ObjectKey, multipart.MultipartId, ct)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "AbortMultipart failed for {Key}; continuing delete", multipart.ObjectKey);
                }
            }

            foreach (var key in payload.Keys)
            {
                // Missing key = success (IFileStore.DeleteAsync is idempotent for absent objects).
                await store.DeleteAsync(key, ct).ConfigureAwait(false);
            }

            job.State = "completed";
            job.CompletedAt = clock.UtcNow;
            job.Error = null;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "object_delete job {JobId} failed", job.Id);
            await ScheduleRetryOrDeadAsync(job, ex.Message, ct).ConfigureAwait(false);
        }
    }

    private async Task ScheduleRetryOrDeadAsync(JobEntity job, string error, CancellationToken ct)
    {
        var maxAttempts = Math.Max(1, options.Value.ObjectDeleteMaxAttempts);
        job.Attempts += 1;
        job.Error = Truncate(error, 2000);
        if (job.Attempts >= maxAttempts)
        {
            job.State = "dead";
            job.CompletedAt = clock.UtcNow;
        }
        else
        {
            var delaySeconds = Math.Min(3600, (int)Math.Pow(2, Math.Min(job.Attempts, 10)));
            job.State = "pending";
            job.AvailableAt = clock.UtcNow.AddSeconds(delaySeconds);
            job.StartedAt = null;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private async Task MarkDeadAsync(JobEntity job, string error, CancellationToken ct)
    {
        job.State = "dead";
        job.Error = Truncate(error, 2000);
        job.CompletedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
