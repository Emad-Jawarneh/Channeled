using Channeled.Application.Abstractions.Email;
using Microsoft.Extensions.Logging;

namespace Channeled.Infrastructure.Email;

public sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(string toEmail, string subject, string body, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Email to {To}: {Subject} — {Body}", toEmail, subject, body);
        return Task.CompletedTask;
    }
}
