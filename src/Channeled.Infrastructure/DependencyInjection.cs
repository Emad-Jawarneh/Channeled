using Channeled.Application.Abstractions.Email;
using Channeled.Application.Abstractions.Operations;
using Channeled.Application.Abstractions.Realtime;
using Channeled.Application.Abstractions.Storage;
using Channeled.Application.Abstractions.Time;
using Channeled.Application.Admin;
using Channeled.Application.Chat;
using Channeled.Application.Files;
using Channeled.Application.Identity;
using Channeled.Application.Notifications;
using Channeled.Application.Personal;
using Channeled.Application.PublicLinks;
using Channeled.Application.Search;
using Channeled.Application.Teams;
using Channeled.Domain.Authorization;
using Channeled.Domain.Identity;
using Channeled.Infrastructure.Admin;
using Channeled.Infrastructure.Chat;
using Channeled.Infrastructure.Email;
using Channeled.Infrastructure.Files;
using Channeled.Infrastructure.Identity;
using Channeled.Infrastructure.Jobs;
using Channeled.Infrastructure.Notifications;
using Channeled.Infrastructure.Operations;
using Channeled.Infrastructure.Persistence;
using Channeled.Infrastructure.Personal;
using Channeled.Infrastructure.PublicLinks;
using Channeled.Infrastructure.Realtime;
using Channeled.Infrastructure.Search;
using Channeled.Infrastructure.Security;
using Channeled.Infrastructure.Storage;
using Channeled.Infrastructure.Teams;
using Channeled.Infrastructure.Time;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Channeled.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddChanneledInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<FileStoreOptions>(configuration.GetSection(FileStoreOptions.SectionName));
        services.Configure<UploadLimitsOptions>(configuration.GetSection(UploadLimitsOptions.SectionName));
        services.Configure<SearchBenchOptions>(configuration.GetSection(SearchBenchOptions.SectionName));
        services.Configure<AuditOptions>(configuration.GetSection(AuditOptions.SectionName));
        services.Configure<BackupOptions>(configuration.GetSection(BackupOptions.SectionName));
        services.Configure<MaintenanceOptions>(configuration.GetSection(MaintenanceOptions.SectionName));
        services.Configure<OperationsOptions>(configuration.GetSection(OperationsOptions.SectionName));
        services.Configure<RealtimeOptions>(configuration.GetSection(RealtimeOptions.SectionName));
        services.Configure<PublicFileLinksOptions>(configuration.GetSection(PublicFileLinksOptions.SectionName));
        services.Configure<ChatOptions>(configuration.GetSection(ChatOptions.SectionName));
        services.Configure<ChannelDeleteOptions>(configuration.GetSection(ChannelDeleteOptions.SectionName));
        services.Configure<UserDeleteOptions>(configuration.GetSection(UserDeleteOptions.SectionName));
        services.Configure<TeamJoinOptions>(configuration.GetSection(TeamJoinOptions.SectionName));

        var connectionString = ResolvePostgresConnectionString(configuration);
        // So backups / other GetConnectionString("Postgres") callers see the same value.
        configuration["ConnectionStrings:Postgres"] = connectionString;

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(connectionString));

        services
            .AddIdentity<AppUser, AppRole>(options =>
            {
                options.Password.RequiredLength = PasswordRules.MinLength;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.User.RequireUniqueEmail = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.AllowedForNewUsers = true;
            })
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

        services.AddMemoryCache();
        services.AddSingleton<SessionValidationCache>();
        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = "channeled.auth";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.SlidingExpiration = true;
            options.ExpireTimeSpan = TimeSpan.FromDays(14);
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
            options.Events.OnValidatePrincipal = SessionCookieEvents.ValidatePrincipalAsync;
        });

        services.Configure<SecurityStampValidatorOptions>(o =>
        {
            o.ValidationInterval = TimeSpan.FromSeconds(5);
        });

        // Data Protection encrypts join tokens, download tickets, etc. Persist the key ring when
        // DataProtection:KeysPath is set (durable cookies across restarts). If the path is not
        // writable (common on ephemeral PaaS mounts), fall back to the default ephemeral ring
        // instead of crashing startup.
        var dataProtection = services.AddDataProtection();
        var keysPath = configuration["DataProtection:KeysPath"];
        if (!string.IsNullOrWhiteSpace(keysPath))
        {
            try
            {
                Directory.CreateDirectory(keysPath);
                dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // Ephemeral keys: sessions reset on restart, but the process stays up.
            }
        }

        services.AddSingleton<TeamJoinLinkProtector>();

        services.AddSingleton<IClock, SystemClock>();
        services.Configure<AccountOptions>(configuration.GetSection(AccountOptions.SectionName));
        RegisterEmailSender(services, configuration);
        services.AddSingleton<IAuthorizer, Authorizer>();
        services.AddSingleton<HubConnectionRegistry>();
        services.AddSingleton<PresenceTracker>();
        services.AddScoped<IChatService, ChatService>();
        services.AddScoped<IChatPurgeService, ChatPurgeService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddSingleton<IChannelNotifier, NullChannelNotifier>();
        services.AddSingleton<IAuthStateChanged, NullAuthStateChanged>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IProfileService, ProfileService>();
        services.AddScoped<ISelfServiceMail, SelfServiceMail>();
        services.AddScoped<IAccountMailService, AccountMailService>();
        services.AddScoped<IEmailSendJobProcessor, EmailSendJobProcessor>();
        services.AddScoped<ChannelWriteGuard>();
        services.AddScoped<ITeamService, TeamService>();
        services.AddScoped<IFileService, FileService>();
        services.Configure<DownloadOptions>(configuration.GetSection(DownloadOptions.SectionName));
        services.AddSingleton<IDownloadSlots, DownloadSlots>();
        services.AddScoped<IDownloadTicketService, DownloadTicketService>();
        services.AddScoped<IFileReferenceService, FileReferenceService>();
        services.AddScoped<IPersonalService, PersonalService>();
        services.AddScoped<Channeled.Application.Clipboard.IClipboardService, Clipboard.ClipboardService>();
        services.AddScoped<ISearchService, SearchService>();
        services.AddScoped<IUsageService, UsageService>();
        services.AddScoped<IQuotaReconciliationService, QuotaReconciliationService>();
        services.AddScoped<IAdminService, AdminService>();
        services.AddScoped<IObjectDeleteJobProcessor, ObjectDeleteJobProcessor>();
        services.AddScoped<ITrashPurgeService, TrashPurgeService>();
        services.AddSingleton<HmacTicketSigner>();
        services.AddSingleton<ClaimIdempotencyStore>();
        services.AddSingleton<TicketUsageTracker>();
        services.AddSingleton<IAnonDownloadLimiter, AnonDownloadLimiter>();
        services.AddScoped<ShareLinkService>();
        services.AddScoped<IShareLinkService>(sp => sp.GetRequiredService<ShareLinkService>());
        services.AddSingleton<IMaintenanceMode, MaintenanceMode>();
        services.AddSingleton<IOperatorAlertService, OperatorAlertService>();
        services.AddScoped<IStoreReconciliationService, StoreReconciliationService>();
        services.AddScoped<IBackupService, BackupService>();
        services.AddScoped<ISystemHealthService, SystemHealthService>();
        services.AddSingleton<IFileStore>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<FileStoreOptions>>().Value;
            return options.Kind switch
            {
                FileStoreKind.FileSystem => new FilesystemFileStore(options),
                FileStoreKind.S3Compatible => new S3FileStore(options),
                _ => throw new InvalidOperationException($"Unknown file store kind '{options.Kind}'."),
            };
        });

        if (!configuration.GetValue("DisableBackgroundJobs", false))
        {
            services.AddHostedService<QuotaReconciliationHostedService>();
            services.AddHostedService<BackupHostedService>();
            services.AddHostedService<ChatPurgeHostedService>();
            services.AddHostedService<ObjectDeleteHostedService>();
            services.AddHostedService<EmailSendHostedService>();
            services.AddHostedService<TrashPurgeHostedService>();
            services.AddHostedService<UserDeleteSweepHostedService>();
            services.AddHostedService<UsernameReservationSweepHostedService>();
        }

        return services;
    }

    private static void RegisterEmailSender(IServiceCollection services, IConfiguration configuration)
    {
        var host = configuration[$"{OperationsOptions.SectionName}:SmtpHost"];
        var useSmtp = !string.IsNullOrWhiteSpace(host);
        if (useSmtp)
        {
            services.AddSingleton<IEmailSender, SmtpEmailSender>();
            services.AddSingleton<IEmailSenderMarker>(_ => new EmailSenderMarker(true));
        }
        else
        {
            services.AddSingleton<IEmailSender, LoggingEmailSender>();
            services.AddSingleton<IEmailSenderMarker>(_ => new EmailSenderMarker(false));
        }
    }

    /// <summary>
    /// Railway / libpq often supply <c>postgresql://...</c> (or <c>DATABASE_URL</c>).
    /// Npgsql's EF path rejects URI form with "Format ... index 0"; convert to keyword form.
    /// Prefers PaaS URLs / libpq env so Production never silently uses a localhost appsettings default.
    /// </summary>
    internal static string ResolvePostgresConnectionString(IConfiguration configuration)
    {
        var databaseUrl = FirstNonEmpty(
            configuration["DATABASE_URL"],
            Environment.GetEnvironmentVariable("DATABASE_URL"),
            configuration["DATABASE_PRIVATE_URL"],
            Environment.GetEnvironmentVariable("DATABASE_PRIVATE_URL"),
            configuration["POSTGRES_URL"],
            Environment.GetEnvironmentVariable("POSTGRES_URL"));
        var fromLibpq = TryBuildConnectionStringFromLibpqEnv(configuration);
        var configured = configuration.GetConnectionString("Postgres");

        // Skip baked-in local defaults when a PaaS URL is present (Docker/Railway Production).
        var hasRemote = !string.IsNullOrWhiteSpace(databaseUrl) || !string.IsNullOrWhiteSpace(fromLibpq);
        if (IsLocalDevPostgresPlaceholder(configured) && hasRemote)
        {
            configured = null;
        }

        var raw = FirstNonEmpty(databaseUrl, fromLibpq, configured)
                  ?? throw new InvalidOperationException(
                      "Postgres is required. On Railway: add a Postgres service, then on this app service set "
                      + "DATABASE_URL=${{Postgres.DATABASE_URL}} (use the exact Postgres service name). "
                      + "Or set ConnectionStrings__Postgres. Development localhost defaults are not used in Production.");

        return NormalizeNpgsqlConnectionString(raw);
    }

    /// <summary>Assemble keyword CS from Railway/libpq <c>PGHOST</c>/<c>PGUSER</c>/… when URL vars are absent.</summary>
    internal static string? TryBuildConnectionStringFromLibpqEnv(IConfiguration configuration)
    {
        static string? Env(IConfiguration config, string key) =>
            FirstNonEmpty(config[key], Environment.GetEnvironmentVariable(key));

        var host = Env(configuration, "PGHOST");
        var user = Env(configuration, "PGUSER");
        var password = Env(configuration, "PGPASSWORD");
        var database = Env(configuration, "PGDATABASE");
        if (string.IsNullOrWhiteSpace(host)
            || string.IsNullOrWhiteSpace(user)
            || password is null
            || string.IsNullOrWhiteSpace(database))
        {
            return null;
        }

        var port = Env(configuration, "PGPORT") ?? "5432";
        return $"Host={host};Port={port};Database={database};Username={user};Password={password};SslMode=Prefer";
    }

    internal static string NormalizeNpgsqlConnectionString(string connectionString)
    {
        var cs = connectionString.Trim().Trim('"');
        if (!cs.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
            && !cs.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return cs;
        }

        // Uri requires a scheme it knows; postgres:// works. Keep postgresql:// as-is for Uri.
        var parseable = cs.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
            ? cs
            : "postgres://" + cs["postgresql://".Length..];
        var uri = new Uri(parseable);
        var userInfo = uri.UserInfo.Split(':', 2);
        var user = Uri.UnescapeDataString(userInfo[0]);
        var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "";
        var database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
        var port = uri.IsDefaultPort ? 5432 : uri.Port;
        var sslMode = ResolveSslModeFromUriQuery(uri.Query);

        return $"Host={uri.Host};Port={port};Database={database};Username={user};Password={password};SslMode={sslMode}";
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>Local appsettings / compose defaults that must not win over PaaS DATABASE_URL.</summary>
    internal static bool IsLocalDevPostgresPlaceholder(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return false;
        }

        var n = connectionString.ToLowerInvariant();
        var localHost = n.Contains("127.0.0.1", StringComparison.Ordinal)
                        || n.Contains("localhost", StringComparison.Ordinal);
        if (!localHost)
        {
            return false;
        }

        return n.Contains("database=channeled", StringComparison.Ordinal)
               || n.Contains("channeled_dev", StringComparison.Ordinal);
    }

    private static string ResolveSslModeFromUriQuery(string query)
    {
        // Prefer works for local Docker and Railway private networking; honor explicit sslmode=.
        const string fallback = "Prefer";
        if (string.IsNullOrEmpty(query))
        {
            return fallback;
        }

        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length != 2 || !kv[0].Equals("sslmode", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = Uri.UnescapeDataString(kv[1]);
            if (value.Equals("disable", StringComparison.OrdinalIgnoreCase)
                || value.Equals("allow", StringComparison.OrdinalIgnoreCase)
                || value.Equals("prefer", StringComparison.OrdinalIgnoreCase)
                || value.Equals("require", StringComparison.OrdinalIgnoreCase)
                || value.Equals("verify-ca", StringComparison.OrdinalIgnoreCase)
                || value.Equals("verify-full", StringComparison.OrdinalIgnoreCase))
            {
                // Npgsql enum uses VerifyCA / VerifyFull; keyword form accepts hyphenated libpq names via Prefer/Require mostly.
                return value.Equals("verify-ca", StringComparison.OrdinalIgnoreCase) ? "VerifyCA"
                    : value.Equals("verify-full", StringComparison.OrdinalIgnoreCase) ? "VerifyFull"
                    : char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();
            }
        }

        return fallback;
    }
}
