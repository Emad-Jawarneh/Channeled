using System.Security.Cryptography;
using System.Text;
using Channeled.Application.Abstractions.Operations;
using Channeled.Application.Abstractions.Storage;
using Channeled.Application.Abstractions.Time;
using Channeled.Domain.Identity;
using Channeled.Domain.Storage;
using Channeled.Infrastructure.Email;
using Channeled.Infrastructure.Files;
using Channeled.Infrastructure.Identity;
using Channeled.Infrastructure.Operations;
using Channeled.Infrastructure.Persistence;
using Channeled.Infrastructure.Storage;
using Channeled.Infrastructure.Time;
using Channeled.Tests.Integration.Search;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Channeled.Tests.Integration.Operations;

/// <summary>
/// M7 gate: scripted restore on a wiped store (+ pg_restore --clean) reproduces data with zero reconcile diffs.
/// Shares SearchBench collection so it does not race the 1M-file bench on the same database.
/// </summary>
[Collection("SearchBench")]
public class RestoreDrillTests
{
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("ConnectionStrings__PostgresRestoreDrill")
        ?? Environment.GetEnvironmentVariable("ConnectionStrings__PostgresTest")
        ?? "Host=127.0.0.1;Port=5432;Database=channeled_test;Username=channeled;Password=channeled_dev";

    [Fact]
    public async Task Scripted_restore_on_clean_store_reproduces_data_with_zero_reconcile_diffs()
    {
        var storeRoot = Path.Combine(Path.GetTempPath(), "channeled-drill-store-" + Guid.NewGuid().ToString("N"));
        var backupRoot = Path.Combine(Path.GetTempPath(), "channeled-drill-backup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(storeRoot);
        Directory.CreateDirectory(backupRoot);

        try
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Postgres"] = ConnectionString,
                    ["FileStore:Kind"] = "FileSystem",
                    ["FileStore:RootPath"] = storeRoot,
                    ["Backup:RootPath"] = backupRoot,
                    ["Backup:RestoreViaAdmin"] = "true",
                })
                .Build();

            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(config);
            services.AddLogging();
            services.AddSingleton<IClock, SystemClock>();
            services.Configure<FileStoreOptions>(config.GetSection(FileStoreOptions.SectionName));
            services.Configure<BackupOptions>(config.GetSection(BackupOptions.SectionName));
            services.Configure<MaintenanceOptions>(_ => { });
            services.AddDbContext<ApplicationDbContext>(o => o.UseNpgsql(ConnectionString));
            services
                .AddIdentity<AppUser, AppRole>()
                .AddEntityFrameworkStores<ApplicationDbContext>()
                .AddDefaultTokenProviders();
            services.AddSingleton<IFileStore>(sp =>
            {
                var opts = sp.GetRequiredService<IOptions<FileStoreOptions>>().Value;
                return new FilesystemFileStore(opts);
            });
            services.AddSingleton<IMaintenanceMode, MaintenanceMode>();
            services.AddSingleton<IOperatorAlertService>(sp =>
                new OperatorAlertService(
                    new LoggingEmailSender(NullLogger<LoggingEmailSender>.Instance),
                    sp.GetRequiredService<IOptions<BackupOptions>>(),
                    NullLogger<OperatorAlertService>.Instance));
            services.AddScoped<IStoreReconciliationService, StoreReconciliationService>();
            services.AddScoped<IBackupService, BackupService>();

            await using var provider = services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<ApplicationDbContext>();
            await db.Database.MigrateAsync();
            await TruncateAppDataAsync(db);

            var store = sp.GetRequiredService<IFileStore>();
            var clock = sp.GetRequiredService<IClock>();
            var users = sp.GetRequiredService<UserManager<AppUser>>();
            var backups = sp.GetRequiredService<IBackupService>();
            var reconcile = sp.GetRequiredService<IStoreReconciliationService>();

            var user = new AppUser
            {
                Id = UuidV7.NewGuid(),
                Email = "drill@example.com",
                UserName = "drill@example.com",
                DisplayName = "Drill",
                    UsernameSlug = "drill",
                EmailConfirmed = true,
                Status = "active",
            };
            (await users.CreateAsync(user, "DrillPassword1!")).Succeeded.Should().BeTrue();

            var key = ObjectKey.ForFile(UuidV7.NewGuid());
            var payload = Encoding.UTF8.GetBytes("restore-drill-payload-" + Guid.NewGuid());
            await using (var input = new MemoryStream(payload))
            {
                await store.PutAsync(key, input, "text/plain");
            }

            string sha;
            await using (var read = await store.OpenReadAsync(key))
            {
                sha = Convert.ToHexString(await SHA256.HashDataAsync(read)).ToLowerInvariant();
            }

            db.Files.Add(new FileEntity
            {
                Id = UuidV7.NewGuid(),
                OwnerUserId = user.Id,
                Name = "drill.txt",
                ContentType = "text/plain",
                SizeBytes = payload.Length,
                Sha256 = sha,
                StorageKey = key,
                UploadedBy = user.Id,
                CreatedAt = clock.UtcNow,
                UpdatedAt = clock.UtcNow,
            });
            await db.SaveChangesAsync();

            var package = await backups.CreateAsync("ondemand");
            package.State.Should().Be("verified");

            foreach (var objectKey in await store.ListObjectKeysAsync())
            {
                await store.DeleteAsync(objectKey);
            }

            (await store.ListObjectKeysAsync()).Should().BeEmpty();

            var restored = await backups.RestoreAsync(package.Id);
            restored.State.Should().Be("restored");

            var report = await reconcile.ReconcileAsync();
            report.IsClean.Should().BeTrue(
                because: $"missing={string.Join(',', report.MissingInStore)} orphans={string.Join(',', report.OrphansInStore)} checksum={string.Join(',', report.ChecksumMismatches)}");

            await using var verify = await store.OpenReadAsync(key);
            using var buf = new MemoryStream();
            await verify.CopyToAsync(buf);
            buf.ToArray().Should().Equal(payload);
        }
        finally
        {
            try
            {
                if (Directory.Exists(storeRoot))
                {
                    Directory.Delete(storeRoot, true);
                }

                if (Directory.Exists(backupRoot))
                {
                    Directory.Delete(backupRoot, true);
                }
            }
            catch
            {
                // ignore cleanup
            }
        }
    }

    private static async Task TruncateAppDataAsync(ApplicationDbContext db)
    {
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
