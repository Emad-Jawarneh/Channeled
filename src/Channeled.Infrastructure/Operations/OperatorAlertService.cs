using Channeled.Application.Abstractions.Email;
using Channeled.Application.Abstractions.Operations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Channeled.Infrastructure.Operations;

public sealed class OperatorAlertService(
    IEmailSender emailSender,
    IOptions<BackupOptions> backupOptions,
    ILogger<OperatorAlertService> logger) : IOperatorAlertService
{
    public async Task AlertAsync(string subject, string body, CancellationToken cancellationToken = default)
    {
        var to = backupOptions.Value.AlertEmail;
        if (string.IsNullOrWhiteSpace(to))
        {
            logger.LogWarning("Operator alert (no AlertEmail configured): {Subject} — {Body}", subject, body);
            return;
        }

        await emailSender.SendAsync(to, $"[Channeled] {subject}", body, cancellationToken);
        logger.LogInformation("Operator alert emailed to {To}: {Subject}", to, subject);
    }
}