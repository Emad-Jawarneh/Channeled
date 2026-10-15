using System.Diagnostics;
using Channeled.Application.Files;
using Channeled.Application.Search;
using Channeled.Domain.Authorization;
using Channeled.Domain.Identity;
using Channeled.Domain.Search;
using Channeled.Infrastructure.Files;
using Channeled.Infrastructure.Persistence;
using Channeled.Infrastructure.Search;
using Channeled.Infrastructure.Time;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Channeled.Tests.Integration.Search;

/// <summary>Seeds the 1M-file bench dataset once per run (CHANNELED_BENCH_FILE_COUNT overrides the size).</summary>
public sealed class MillionFileBenchFixture : IAsyncLifetime
{
    public static string ConnectionString =>
        Environment.GetEnvironmentVariable("ConnectionStrings__PostgresTest")
        ?? "Host=127.0.0.1;Port=5432;Database=channeled_test;Username=channeled;Password=channeled_dev";

    public static int SeedCount =>
        int.TryParse(Environment.GetEnvironmentVariable("CHANNELED_BENCH_FILE_COUNT"), out var n) && n > 0
            ? n
            : 1_000_000;

    public ServiceProvider Provider { get; private set; } = null!;
    public Guid UserId { get; private set; }
    public Guid ChannelId { get; private set; }
    public string TeamSlug { get; private set; } = "";

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(o => o.UseNpgsql(ConnectionString));
        services.AddSingleton<IAuthorizer, Authorizer>();
        services.AddSingleton<Channeled.Application.Abstractions.Time.IClock, SystemClock>();
        services.AddScoped<ISearchService, SearchService>();
        services.AddScoped<BenchFileSeeder>();
        Provider = services.BuildServiceProvider();

        await using var scope = Provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Database.SetCommandTimeout(TimeSpan.FromMinutes(15));
        await db.Database.MigrateAsync();

        await db.Database.ExecuteSqlRawAsync("""
            TRUNCATE TABLE "notifications", "message_mentions", "channel_messages",
              "upload_parts", "upload_sessions", "file_stars", "files", "folders",
              "channel_join_requests", "channel_members", "channels", "team_invitations", "team_members", "teams", "jobs"
            RESTART IDENTITY CASCADE;
            """);

        UserId = UuidV7.NewGuid();
        const string email = "bench-int@example.com";
        const string normalized = "BENCH-INT@EXAMPLE.COM";
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""DELETE FROM "AspNetUsers" WHERE "NormalizedEmail" = {normalized}""");

        var quota = 20L * 1024 * 1024 * 1024;
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO "AspNetUsers" (
               "Id", "UserName", "NormalizedUserName", "Email", "NormalizedEmail", "EmailConfirmed",
               "PasswordHash", "SecurityStamp", "ConcurrencyStamp", "PhoneNumberConfirmed", "TwoFactorEnabled",
               "LockoutEnabled", "AccessFailedCount", "DisplayName", "IsPlatformAdmin", "PersonalQuotaBytes",
               "PersonalUsageBytes", "Status")
             VALUES (
               {UserId}, {email}, {normalized}, {email}, {normalized},
               TRUE, {"x"}, {"stamp"}, {"conc"}, FALSE, FALSE, TRUE, 0, {"Bench"}, FALSE, {quota}, 0, {"active"})
             """);

        var seeder = scope.ServiceProvider.GetRequiredService<BenchFileSeeder>();
        var seeded = await seeder.SeedAsync(UserId, SeedCount);
        seeded.Inserted.Should().Be(SeedCount);
        ChannelId = seeded.ChannelId;
        TeamSlug = seeded.TeamSlug;
        await db.Database.ExecuteSqlRawAsync("ANALYZE files; ANALYZE folders;");
    }

    public async Task DisposeAsync() => await Provider.DisposeAsync();
}

/// <summary>
/// M5 gate: p95 list/search under target on a seeded 1M-file dataset.
/// Override with CHANNELED_BENCH_FILE_COUNT / CHANNELED_BENCH_P95_MS.
/// </summary>
[Collection("SearchBench")]
public class MillionFileBenchTests(MillionFileBenchFixture bench) : IClassFixture<MillionFileBenchFixture>
{
    private static int P95MaxMs =>
        int.TryParse(Environment.GetEnvironmentVariable("CHANNELED_BENCH_P95_MS"), out var n) && n > 0
            ? n
            : 300;

    [Fact]
    public async Task Channel_search_p95_under_target_on_seeded_dataset()
    {
        await using var scope = bench.Provider.CreateAsyncScope();
        var search = scope.ServiceProvider.GetRequiredService<ISearchService>();
        var query = new SearchQuery("needle", SearchScope.Channel, bench.TeamSlug, "general", null, null, null, 50);
        for (var i = 0; i < 5; i++)
        {
            _ = await search.SearchAsync(bench.UserId, query);
        }

        var samples = new List<double>(40);
        for (var i = 0; i < 40; i++)
        {
            var sw = Stopwatch.StartNew();
            var result = await search.SearchAsync(bench.UserId, query);
            sw.Stop();
            result.Succeeded.Should().BeTrue(result.Message);
            result.Value!.Hits.Should().Contain(h => h.Name.Contains("needle", StringComparison.OrdinalIgnoreCase));
            samples.Add(sw.Elapsed.TotalMilliseconds);
        }

        Percentile(samples, 0.95).Should().BeLessThan(P95MaxMs, because: $"search p95 on {MillionFileBenchFixture.SeedCount} files");
    }

    public static TheoryData<string> Sorts() => new()
    {
        "name_asc", "name_desc", "size_asc", "size_desc", "created_asc", "created_desc",
        "updated_asc", "updated_desc", "type_asc", "type_desc",
    };

    [Theory]
    [MemberData(nameof(Sorts))]
    public async Task Listing_pages_p95_under_target_from_page_one_to_ten_thousand_rows_deep(string sort)
    {
        await using var scope = bench.Provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var first = new ListItemsQuery(null, null, 200, sort, null, null, TrashOnly: false);
        for (var i = 0; i < 3; i++)
        {
            (await ItemListing.PageAsync(db, bench.ChannelId, null, first, default)).Succeeded.Should().BeTrue();
        }

        // 50 pages × 200 rows reach the same depth as page 200 of the UI's 50-row pages.
        var samples = new List<double>(50);
        string? cursor = null;
        long rows = 0;
        for (var page = 0; page < 50; page++)
        {
            var sw = Stopwatch.StartNew();
            var result = await ItemListing.PageAsync(db, bench.ChannelId, null, first with { Cursor = cursor }, default);
            sw.Stop();
            result.Succeeded.Should().BeTrue(result.Message);
            result.Value!.Rows.Should().HaveCount(200);
            rows += result.Value.Rows.Count;
            samples.Add(sw.Elapsed.TotalMilliseconds);
            cursor = result.Value.NextCursor;
        }

        rows.Should().Be(10_000);
        Percentile(samples, 0.95).Should().BeLessThan(P95MaxMs, because: $"{sort} listing p95 on {MillionFileBenchFixture.SeedCount} files");
    }

    private static double Percentile(List<double> samples, double p)
    {
        var sorted = samples.OrderBy(x => x).ToArray();
        var idx = Math.Clamp((int)Math.Ceiling(p * sorted.Length) - 1, 0, sorted.Length - 1);
        return sorted[idx];
    }
}
