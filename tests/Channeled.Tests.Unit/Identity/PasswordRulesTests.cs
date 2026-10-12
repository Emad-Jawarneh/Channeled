using Channeled.Domain.Identity;
using FluentAssertions;

namespace Channeled.Tests.Unit.Identity;

public class PasswordRulesTests
{
    [Fact]
    public void Accepts_long_enough_password()
    {
        PasswordRules.IsValid("twelvechars!").Should().BeTrue();
    }

    [Fact]
    public void Rejects_short_password()
    {
        PasswordRules.IsValid("short").Should().BeFalse();
    }
}
