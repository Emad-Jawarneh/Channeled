using Channeled.Application.Abstractions.Operations;
using Channeled.Application.Abstractions.Time;
using Channeled.Application.Identity;
using Channeled.Domain.Audit;
using Channeled.Domain.Identity;
using Channeled.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Channeled.Infrastructure.Identity;

public sealed class SelfServiceMail(
    IEmailSenderMarker senderMarker,
    IOptionsMonitor<AccountOptions> accountOptions,
    IOptionsMonitor<OperationsOptions> operations,
    ApplicationDbContext db,
    IClock clock) : ISelfServiceMail
{
    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        var mode = accountOptions.CurrentValue.SelfServiceMail;
        if (string.Equals(mode, SelfServiceMailModes.Off, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Stale verified + logging sender ⇒ unavailable.
        if (!senderMarker.IsSmtp)
        {
            return false;
        }

        var settings = await db.OperationsSettings.FirstOrDefaultAsync(ct);
        if (settings?.SmtpVerifiedAt is null)
        {
            return false;
        }

        // Changing SMTP settings clears verification (fingerprint mismatch).
        var current = AccountMailService.Fingerprint(operations.CurrentValue);
        if (!string.Equals(settings.SmtpSettingsFingerprint, current, StringComparison.Ordinal))
        {
            settings.SmtpVerifiedAt = null;
            settings.SmtpSettingsFingerprint = null;
            db.AuditEvents.Add(new AuditEvent
            {
                Id = UuidV7.NewGuid(),
                OccurredAt = clock.UtcNow,
                ActorId = null,
                EventType = AuditEventType.SmtpConfigChange,
                TargetType = "operations",
                TargetId = "smtp",
                DetailsJson = """{"reason":"fingerprint_mismatch"}""",
                Ip = "",
                UserAgent = "",
            });
            await db.SaveChangesAsync(ct);
            return false;
        }

        return true;
    }

    public async Task<DateTimeOffset?> LastSmtpProbeAsync(CancellationToken ct = default)
    {
        if (!await IsAvailableAsync(ct))
        {
            return null;
        }

        var settings = await db.OperationsSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        return settings?.SmtpVerifiedAt;
    }
}

/// <summary>Tiny marker so SelfServiceMail can detect SmtpEmailSender without type tricks on the interface.</summary>
public interface IEmailSenderMarker
{
    bool IsSmtp { get; }
}

public sealed class EmailSenderMarker(bool isSmtp) : IEmailSenderMarker
{
    public bool IsSmtp { get; } = isSmtp;
}
