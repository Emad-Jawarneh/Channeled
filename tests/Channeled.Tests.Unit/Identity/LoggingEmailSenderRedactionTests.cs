using Channeled.Infrastructure.Email;
using FluentAssertions;

namespace Channeled.Tests.Unit.Identity;

public sealed class LoggingEmailSenderRedactionTests
{
    [Fact]
    public void Redacts_hex_tokens_and_fragments()
    {
        var body = "Reset: https://x/reset#token=abcdef0123456789abcdef0123456789abcd\nraw abcdef0123456789abcdef0123456789abcd";
        var redacted = LoggingEmailSender.Redact(body);
        redacted.Should().NotContain("abcdef0123456789abcdef0123456789abcd");
        redacted.Should().Contain("[REDACTED]");
    }
}
