using Channeled.Application.Abstractions.Time;
using Channeled.Application.Search;
using Channeled.Domain.Identity;
using Channeled.Infrastructure.Jobs;
using Channeled.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Channeled.Infrastructure.Search;

public sealed class QuotaReconciliationService(
    ApplicationDbContext db,
    IClock clock) : IQuotaReconciliationService
{
    public async Task<QuotaReconciliationResult> ReconcileAsync(CancellationToken ct = default)
    {
        var job = new JobEntity
        {
            Id = UuidV7.NewGuid(),
            Type = "quota_reconciliation",
            State = "running",
            CreatedAt = clock.UtcNow,
            StartedAt = clock.UtcNow,
        };
        db.Jobs.Add(job);
        await db.SaveChangesAsync(ct);

        try
        {
            // Trash counts toward quota until purged — include DeletedAt IS NOT NULL.
            var teamsUpdated = await db.Database.ExecuteSqlRawAsync(
                """
                UPDATE teams t
                SET "UsageBytes" = COALESCE((
                    SELECT SUM(f."SizeBytes")
                    FROM files f
                    INNER JOIN channels c ON f."ChannelId" = c."Id"
                    WHERE c."TeamId" = t."Id"
                ), 0)
                """,
                ct);

            var usersUpdated = await db.Database.ExecuteSqlRawAsync(
                """
                UPDATE "AspNetUsers" u
                SET "PersonalUsageBytes" = COALESCE((
                    SELECT SUM(f."SizeBytes")
                    FROM files f
                    WHERE f."OwnerUserId" = u."Id"
                ), 0)
                """,
                ct);

            job.State = "completed";
            job.CompletedAt = clock.UtcNow;
            job.DetailsJson = $"{{\"teamsUpdated\":{teamsUpdated},\"usersUpdated\":{usersUpdated}}}";
            await db.SaveChangesAsync(ct);

            return new QuotaReconciliationResult(teamsUpdated, usersUpdated, job.CompletedAt.Value);
        }
        catch (Exception ex)
        {
            job.State = "failed";
            job.CompletedAt = clock.UtcNow;
            job.Error = ex.Message;
            await db.SaveChangesAsync(ct);
            throw;
        }
    }
}
