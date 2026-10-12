using Channeled.Infrastructure.Search;
using FluentAssertions;

namespace Channeled.Tests.Unit.Search;

public class TrigramScoreTests
{
    [Fact]
    public void Exact_substring_scores_higher_than_unrelated()
    {
        SearchService.TrigramScore("quarterly-report.pdf", "report")
            .Should().BeGreaterThan(SearchService.TrigramScore("notes.txt", "report"));
    }
}
