using Channeled.Domain.Identity;
using FluentAssertions;

namespace Channeled.Tests.Unit.Identity;

public class EmailAddressTests
{
    [Fact]
    public void Parse_normalizes_case()
    {
        EmailAddress.Parse("Ops@Example.COM").Value.Should().Be("ops@example.com");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    public void Parse_rejects_invalid(string input)
    {
        var act = () => EmailAddress.Parse(input);
        act.Should().Throw<ArgumentException>();
    }
}
