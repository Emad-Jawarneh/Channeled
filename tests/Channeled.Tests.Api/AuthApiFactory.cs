using Channeled.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Channeled.Tests.Api;

public class AuthApiFactory : WebApplicationFactory<Program>
{
    public static string ConnectionString { get; } =
        Environment.GetEnvironmentVariable("ConnectionStrings__PostgresTest")
        ?? "Host=127.0.0.1;Port=5432;Database=channeled_test;Username=channeled;Password=channeled_dev";

    public const string TestHmacKey = "test-only-hmac-key-0123456789abcdef0123456789abcdef";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = ConnectionString,
                ["MigrateOnStartup"] = "false",
                ["FileStore:Kind"] = "FileSystem",
                ["FileStore:RootPath"] = Path.Combine(Path.GetTempPath(), "channeled-api-tests-" + Guid.NewGuid().ToString("N")),
                ["Backup:RootPath"] = Path.Combine(Path.GetTempPath(), "channeled-api-backups-" + Guid.NewGuid().ToString("N")),
                ["Backup:RestoreViaAdmin"] = "false",
                ["DisableBackgroundJobs"] = "true",
                ["PublicFileLinks:Enabled"] = "true",
                ["PublicFileLinks:HmacKey"] = TestHmacKey,
                ["PublicFileLinks:PreviousHmacKey"] = "",
                ["PublicFileLinks:AnonRequestsPerMinutePerIp"] = "100000",
                // High default so functional chat tests never trip the limiter; the rate-limit test lowers it.
                ["Chat:PostsPerMinutePerUser"] = "100000",
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(DbContextOptions<ApplicationDbContext>));
            services.RemoveAll(typeof(ApplicationDbContext));
            services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(ConnectionString));
        });
    }

    public async Task ResetDatabaseAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync();

        await db.Database.ExecuteSqlRawAsync("""
            TRUNCATE TABLE
              "notifications",
              "message_mentions",
              "file_share_links",
              "channel_messages",
              "backups",
              "jobs",
              "file_stars",
              "upload_parts",
              "upload_sessions",
              "files",
              "folders",
              "channel_join_requests",
              "channel_members",
              "channels",
              "team_invitations",
              "team_members",
              "teams",
              "password_reset_tokens",
              "platform_invitations",
              "sessions",
              "audit_events",
              "AspNetUserTokens",
              "AspNetUserRoles",
              "AspNetUserLogins",
              "AspNetUserClaims",
              "AspNetRoleClaims",
              "AspNetUsers",
              "AspNetRoles"
            RESTART IDENTITY CASCADE;
            """);
    }
}
