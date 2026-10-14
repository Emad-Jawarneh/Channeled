using System.Text.RegularExpressions;
using Channeled.Application.Abstractions.Email;
using Microsoft.Extensions.Logging;

namespace Channeled.Infrastructure.Email;

public sealed partial class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(string toEmail, string subject, string body, CancellationToken cancellationToken = default)
    {
        var redacted = Redact(body);
        logger.LogInformation("Email to {To}: {Subject} — {Body}", toEmail, subject, redacted);
        return Task.CompletedTask;
    }

    public static string Redact(string body)
    {
        if (string.IsNullOrEmpty(body))
        {
            return body;
        }

        var s = HexToken().Replace(body, "[REDACTED]");
        s = QueryToken().Replace(s, "$1[REDACTED]");
        s = FragmentToken().Replace(s, "$1[REDACTED]");
        return s;
    }

    [GeneratedRegex(@"\b[a-f0-9]{32,}\b", RegexOptions.IgnoreCase)]
    private static partial Regex HexToken();

    [GeneratedRegex(@"(token=)[^&\s]+", RegexOptions.IgnoreCase)]
    private static partial Regex QueryToken();

    [GeneratedRegex(@"(#token=)[^\s]+", RegexOptions.IgnoreCase)]
    private static partial Regex FragmentToken();
}
