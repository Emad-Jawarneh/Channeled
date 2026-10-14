using Channeled.Application.Abstractions.Operations;
using Channeled.Infrastructure.Identity;
using FluentAssertions;
using Xunit;

namespace Channeled.Tests.Unit.Identity;

public sealed class SmtpFingerprintTests
{
    [Fact]
    public void Fingerprint_changes_when_smtp_host_changes()
    {
        var a = new OperationsOptions { SmtpHost = "mailpit", SmtpPort = 1025, SmtpFrom = "a@x.test" };
        var b = new OperationsOptions { SmtpHost = "smtp.example", SmtpPort = 1025, SmtpFrom = "a@x.test" };

        AccountMailService.Fingerprint(a).Should().NotBe(AccountMailService.Fingerprint(b));
        AccountMailService.Fingerprint(a).Should().Be(AccountMailService.Fingerprint(a));
    }
}
