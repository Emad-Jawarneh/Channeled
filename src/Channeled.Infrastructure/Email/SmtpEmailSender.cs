using System.Net;
using System.Net.Mail;
using Channeled.Application.Abstractions.Email;
using Channeled.Application.Abstractions.Operations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Channeled.Infrastructure.Email;

public sealed class SmtpEmailSender(
    IOptionsMonitor<OperationsOptions> options,
    ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(string toEmail, string subject, string body, CancellationToken cancellationToken = default)
    {
        var o = options.CurrentValue;
        if (string.IsNullOrWhiteSpace(o.SmtpHost) || string.IsNullOrWhiteSpace(o.SmtpFrom))
        {
            throw new InvalidOperationException("SMTP is not configured.");
        }

        using var message = new MailMessage(o.SmtpFrom, toEmail, subject, body);
        using var client = new SmtpClient(o.SmtpHost, o.SmtpPort)
        {
            EnableSsl = o.SmtpUseStartTls,
            DeliveryMethod = SmtpDeliveryMethod.Network,
        };

        if (!string.IsNullOrWhiteSpace(o.SmtpUsername))
        {
            client.Credentials = new NetworkCredential(o.SmtpUsername, o.SmtpPassword);
        }

        using var reg = cancellationToken.Register(() =>
        {
            try
            {
                client.SendAsyncCancel();
            }
            catch
            {
                // ignore
            }
        });

        logger.LogInformation("SMTP send to {To} subject {Subject}", toEmail, subject);
        await client.SendMailAsync(message, cancellationToken);
    }
}
