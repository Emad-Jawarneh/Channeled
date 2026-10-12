using System.Diagnostics;
using Channeled.Application.Search;
using Channeled.Domain.Authorization;
using Channeled.Domain.Identity;
using Channeled.Domain.Search;
using Channeled.Infrastructure.Persistence;
using Channeled.Infrastructure.Search;
using Channeled.Infrastructure.Time;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Channeled.Tests.Integration.Search;

/// <summary>
/// M5 gate: p95 list/search under target on a seeded 1M-file dataset.
/// Override with CHANNELED_BENCH_FILE_COUNT / CHANNELED_BENCH_P95_MS.
/// </summary>
[Collection("SearchBench")]
public class MillionFileBenchTests
{
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("ConnectionStrings__PostgresTest")
        ?? "Host=127.0.0.1;Port=5432;Database=channeled_test;Username=channeled;Password=channeled_dev";

    private static int SeedCount =>
        int.TryParse(Environment.GetEnvironmentVariable("CHANNELED_BENCH_FILE_COUNT"), out var n) && n > 0
            ? n
            : 1_000_000;

    private static int P95MaxMs =>
        int.TryParse(Environment.GetEnvironmentVariable("CHANNELED_BENCH_P95_MS"), out var n) && n > 0
            ? n
            : 500;

    [Fact]
    public async Task List_and_search_p95_under_target_on_seeded_dataset()
    {
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(o => o.UseNpgsql(ConnectionString));
        services.AddSingleton<IAuthorizer, Authorizer>();
        services.AddSingleton<Channeled.Application.Abstractions.Time.IClock, SystemClock>();
        services.AddScoped<ISearchService, SearchService>();
        services.AddScoped<BenchFileSeeder>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Database.SetCommandTimeout(TimeSpan.FromMinutes(15));
        await db.Database.MigrateAsync();

        await db.Database.ExecuteSqlRawAsync("""
            TRUNCATE TABLE "upload_parts", "upload_sessions", "file_stars", "files", "folders",
              "channel_join_requests", "channel_members", "channels", "team_invitations", "team_members", "teams", "jobs"
            RESTART IDENTITY CASCADE;
            """);

        var userId = UuidV7.NewGuid();
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
               {userId}, {email}, {normalized}, {email}, {normalized},
               TRUE, {"x"}, {"stamp"}, {"conc"}, FALSE, FALSE, TRUE, 0, {"Bench"}, FALSE, {quota}, 0, {"active"})
             """);

        var seeder = scope.ServiceProvider.GetRequiredService<BenchFileSeeder>();
        var seeded = await seeder.SeedAsync(userId, SeedCount);
        seeded.Inserted.Should().Be(SeedCount);

        _ = await db.Files.AsNoTracking().Where(f => f.ChannelId == seeded.ChannelId).Take(1).ToListAsync();
        _ = await db.Files.AsNoTracking()
            .Where(f => f.ChannelId == seeded.ChannelId && EF.Functions.ILike(f.Name, "%needle%"))
            .Take(5)
            .ToListAsync();

        await db.Database.ExecuteSqlRawAsync("ANALYZE files;");

        var listSamples = new List<double>(40);
        var searchSamples = new List<double>(40);
        var search = scope.ServiceProvider.GetRequiredService<ISearchService>();

        // Warm-up rounds excluded from p95.
        for (var i = 0; i < 5; i++)
        {
            _ = await db.Files.AsNoTracking()
                .Where(f => f.ChannelId == seeded.ChannelId && f.DeletedAt == null)
                .OrderByDescending(f => f.UpdatedAt)
                .Take(50)
                .ToListAsync();
            _ = await search.SearchAsync(
                userId,
                new SearchQuery("needle", SearchScope.Channel, seeded.TeamSlug, "general", null, null, null, 50));
        }

        for (var i = 0; i < 40; i++)
        {
            var sw = Stopwatch.StartNew();
            var page = await db.Files.AsNoTracking()
                .Where(f => f.ChannelId == seeded.ChannelId && f.DeletedAt == null)
                .OrderByDescending(f => f.UpdatedAt)
                .Take(50)
                .ToListAsync();
            sw.Stop();
            page.Should().HaveCount(50);
            listSamples.Add(sw.Elapsed.TotalMilliseconds);

            sw.Restart();
            var result = await search.SearchAsync(
                userId,
                new SearchQuery("needle", SearchScope.Channel, seeded.TeamSlug, "general", null, null, null, 50));
            sw.Stop();
            result.Succeeded.Should().BeTrue(result.Message);
            result.Value!.Hits.Should().Contain(h => h.Name.Contains("needle", StringComparison.OrdinalIgnoreCase));
            searchSamples.Add(sw.Elapsed.TotalMilliseconds);
        }

        Percentile(listSamples, 0.95).Should().BeLessThan(P95MaxMs, because: $"list p95 on {SeedCount} files");
        Percentile(searchSamples, 0.95).Should().BeLessThan(P95MaxMs, because: $"search p95 on {SeedCount} files");
    }

    private static double Percentile(List<double> samples, double p)
    {
        var sorted = samples.OrderBy(x => x).ToArray();
        var idx = Math.Clamp((int)Math.Ceiling(p * sorted.Length) - 1, 0, sorted.Length - 1);
        return sorted[idx];
    }
}
