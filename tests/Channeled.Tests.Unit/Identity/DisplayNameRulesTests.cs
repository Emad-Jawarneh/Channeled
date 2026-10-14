using Channeled.Domain.Identity;
using FluentAssertions;

namespace Channeled.Tests.Unit.Identity;

public sealed class DisplayNameRulesTests
{
    [Fact]
    public void Strips_zero_width_and_normalizes()
    {
        var cleaned = DisplayNameRules.Cleanup("A\u200Bbc");
        cleaned.Should().Be("Abc");
    }

    [Fact]
    public void Migration_fallback_when_empty()
    {
        var name = DisplayNameRules.SanitizeForMigration("\u200B", "alice@example.com");
        name.Should().NotBeNullOrWhiteSpace();
    }
}
