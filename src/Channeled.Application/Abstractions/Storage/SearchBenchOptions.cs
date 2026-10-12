namespace Channeled.Application.Abstractions.Storage;

/// <summary>M5 list/search latency targets on the seeded bench dataset.</summary>
public sealed class SearchBenchOptions
{
    public const string SectionName = "SearchBench";

    /// <summary>p95 ceiling in milliseconds for list and search.</summary>
    public int P95MaxMilliseconds { get; set; } = 500;

    public int DefaultSeedCount { get; set; } = 1_000_000;
}
