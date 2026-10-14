using Channeled.Domain.Identity;
using FluentAssertions;

namespace Channeled.Tests.Unit.Identity;

public sealed class UsernameRulesTests
{
    [Theory]
    [InlineData("abc")]
    [InlineData("a.b_c-d")]
    [InlineData("user.name")]
    public void Accepts_valid(string username)
    {
        UsernameRules.TryValidate(username, out var n, out var err).Should().BeTrue();
        n.Should().Be(username.ToLowerInvariant());
        err.Should().BeNull();
    }

    [Theory]
    [InlineData("ab")]
    [InlineData(".abc")]
    [InlineData("abc.")]
    [InlineData("a..b")]
    [InlineData("admin")]
    [InlineData("Bad Name")]
    public void Rejects_invalid(string username)
    {
        UsernameRules.TryValidate(username, out _, out var err).Should().BeFalse();
        err.Should().NotBeNullOrWhiteSpace();
    }
}
