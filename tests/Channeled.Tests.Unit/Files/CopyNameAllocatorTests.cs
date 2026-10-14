using Channeled.Domain.Files;
using FluentAssertions;

namespace Channeled.Tests.Unit.Files;

public class CopyNameAllocatorTests
{
    [Fact]
    public void Allocate_returns_original_when_unique()
    {
        CopyNameAllocator.Allocate("report.pdf", ["other.txt"]).Should().Be("report.pdf");
    }

    [Fact]
    public void Allocate_uses_copy_suffix_before_extension()
    {
        CopyNameAllocator.Allocate("report.pdf", ["report.pdf"])
            .Should().Be("report (copy).pdf");
    }

    [Fact]
    public void Allocate_increments_copy_number()
    {
        var existing = new[] { "report.pdf", "report (copy).pdf" };
        CopyNameAllocator.Allocate("report.pdf", existing).Should().Be("report (copy 2).pdf");
    }

    [Fact]
    public void Allocate_is_case_insensitive_against_siblings()
    {
        CopyNameAllocator.Allocate("Report.PDF", ["report.pdf"]).Should().Be("Report (copy).PDF");
    }

    [Fact]
    public void Allocate_works_for_folder_names_without_extension()
    {
        CopyNameAllocator.Allocate("Docs", ["Docs"]).Should().Be("Docs (copy)");
    }
}
