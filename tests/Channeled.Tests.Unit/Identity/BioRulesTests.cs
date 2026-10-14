using Channeled.Domain.Identity;
using FluentAssertions;
using Xunit;

namespace Channeled.Tests.Unit.Identity;

public sealed class BioRulesTests
{
    [Fact]
    public void Empty_bio_is_allowed()
    {
        BioRules.TryValidate("   ", out var cleaned, out var error).Should().BeTrue();
        cleaned.Should().BeEmpty();
        error.Should().BeNull();
    }

    [Fact]
    public void Strips_zero_width_and_truncates()
    {
        var raw = "Hello\u200B " + new string('a', 300);
        BioRules.TryValidate(raw, out var cleaned, out _).Should().BeTrue();
        cleaned.Should().NotContain("\u200B");
        cleaned.Length.Should().Be(BioRules.MaxLength);
    }
}
