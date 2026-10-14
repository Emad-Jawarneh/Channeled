using Channeled.Application.Abstractions.Email;
using Channeled.Application.Abstractions.Time;
using Channeled.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Channeled.Infrastructure.Jobs;

public interface IEmailSendJobProcessor
{
    Task<int> ProcessAsync(int maxJobs, CancellationToken ct);
}

public sealed class EmailSendJobProcessor(
    ApplicationDbContext db,
    IEmailSender emailSender,
    IDataProtectionProvider dataProtection,
    IClock clock,
    ILogger<EmailSendJobProcessor> logger) : IEmailSendJobProcessor
{
    public async Task<int> ProcessAsync(int maxJobs, CancellationToken ct)
    {
        var processed = 0;
        for (var i = 0; i < maxJobs; i++)
        {
            var job = await db.Jobs
                .Where(j => j.Type == EmailSendJobTypes.Type && j.State == "pending" && j.AvailableAt <= clock.UtcNow)
                .OrderBy(j => j.AvailableAt)
                .FirstOrDefaultAsync(ct);
            if (job is null)
            {
                break;
            }

            if (!EmailSendOutbox.TryUnprotect(dataProtection, job.DetailsJson, out var payload, out var decryptFailed))
            {
                if (decryptFailed)
                {
                    job.State = "dead";
                    job.Error = EmailSendJobTypes.DeadReasonDecrypt;
                    job.DetailsJson = """{"protectedPayload":"[REDACTED]"}""";
                    await db.SaveChangesAsync(ct);
                    logger.LogError("Email job {JobId} dead-lettered: {Reason}", job.Id, job.Error);
                    processed++;
                    continue;
                }

                job.State = "dead";
                job.Error = "invalid email job payload";
                await db.SaveChangesAsync(ct);
                processed++;
                continue;
            }

            if (payload!.TokenExpiresAt is { } exp && exp < clock.UtcNow)
            {
                job.State = "completed";
                job.DetailsJson = """{"protectedPayload":"[REDACTED]"}""";
                await db.SaveChangesAsync(ct);
                processed++;
                continue;
            }

            try
            {
                await emailSender.SendAsync(payload.To, payload.Subject, payload.Body, ct);
                job.State = "completed";
                job.DetailsJson = """{"protectedPayload":"[REDACTED]"}""";
                job.Error = null;
            }
            catch (Exception ex)
            {
                job.Attempts++;
                job.Error = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
                if (job.Attempts >= 8)
                {
                    job.State = "dead";
                }
                else
                {
                    job.AvailableAt = clock.UtcNow.Add(TimeSpan.FromSeconds(Math.Pow(2, Math.Min(job.Attempts, 6))));
                }
            }

            await db.SaveChangesAsync(ct);
            processed++;
        }

        return processed;
    }
}
