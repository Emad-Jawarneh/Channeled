using Channeled.Domain.Files;
using FluentAssertions;

namespace Channeled.Tests.Unit.Files;

public class SiblingNameTests
{
    [Theory]
    [InlineData("Report.pdf", "report.PDF", true)]
    [InlineData("a", "b", false)]
    [InlineData("  Docs ", "docs", true)]
    public void EqualsIgnoreCase_matches_spec(string a, string b, bool expected) =>
        SiblingName.EqualsIgnoreCase(a, b).Should().Be(expected);
}
