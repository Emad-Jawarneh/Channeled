using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Channeled.Api.Admin;
using Channeled.Api.Auth;
using Channeled.Api.Chat;
using Channeled.Api.Files;
using Channeled.Api.Personal;
using Channeled.Api.PublicLinks;
using Channeled.Api.Notifications;
using Channeled.Api.Realtime;
using Channeled.Api.Search;
using Channeled.Api.Teams;
using Channeled.Api.Users;
using Channeled.Api.Account;
using Channeled.Application.Abstractions.Operations;
using Channeled.Application.Abstractions.Realtime;
using Channeled.Application.Identity;
using Channeled.Domain.Identity;
using Channeled.Infrastructure;
using Channeled.Infrastructure.Identity;
using Channeled.Infrastructure.Persistence;
using Channeled.Infrastructure.Realtime;
using Channeled.Infrastructure.Search;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Multipart part uploads stream the body; keep headroom above PartSizeBytes (default 8 MiB).
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 128L * 1024 * 1024;
});

builder.Services.AddChanneledInfrastructure(builder.Configuration);
builder.Services.AddOpenApi();
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "channeled.csrf";
    options.Cookie.HttpOnly = false;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // Configurable so the API test suite (many logins per minute) is not throttled; prod default stays 20/min.
    // Partitioned per IP so one client cannot exhaust the global budget.
    options.AddPolicy("auth", httpContext =>
    {
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var permit = Math.Max(1, builder.Configuration.GetValue("RateLimits:AuthPermitPerMinute", 20));
        return RateLimitPartition.GetFixedWindowLimiter(
            $"auth:{ip}",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            });
    });

    // Token POSTs: partition by IP and token (body) so brute-force is limited per-token too.
    options.AddPolicy("auth-token", httpContext =>
    {
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var tokenKey = PeekJsonTokenKey(httpContext.Request);
        return RateLimitPartition.GetFixedWindowLimiter(
            $"tok:{ip}:{tokenKey}",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            });
    });

    // Anonymous public share endpoints: per-IP budget (read lazily so IOptionsMonitor/test config applies).
    options.AddPolicy(ShareLinkEndpoints.AnonRateLimitPolicy, httpContext =>
    {
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var permit = httpContext.RequestServices
            .GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<Channeled.Application.PublicLinks.PublicFileLinksOptions>>()
            .CurrentValue.AnonRequestsPerMinutePerIp;
        return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = Math.Max(1, permit),
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        });
    });

    // Team join link info/join: per-user when signed in, else per-IP. Limits token guessing.
    options.AddPolicy(TeamEndpoints.JoinRateLimitPolicy, httpContext =>
    {
        var key = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                  ?? httpContext.Connection.RemoteIpAddress?.ToString()
                  ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 30,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        });
    });

    // Chat posts: per-user budget. Requires authentication to run BEFORE the limiter (see pipeline order below).
    options.AddPolicy(ChatEndpoints.PostRateLimitPolicy, httpContext =>
    {
        var userKey = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                      ?? httpContext.Connection.RemoteIpAddress?.ToString()
                      ?? "unknown";
        var permit = httpContext.RequestServices
            .GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<Channeled.Application.Chat.ChatOptions>>()
            .CurrentValue.PostsPerMinutePerUser;
        return RateLimitPartition.GetFixedWindowLimiter(userKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = Math.Max(1, permit),
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        });
    });
});

builder.Services.AddAuthorization();

var realtimeOpts = builder.Configuration.GetSection(RealtimeOptions.SectionName).Get<RealtimeOptions>()
    ?? new RealtimeOptions();
builder.Services.AddSignalR(options =>
{
    options.MaximumReceiveMessageSize = realtimeOpts.MaxInboundMessageBytes;
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
}).AddHubOptions<ChannelHub>(options =>
{
    options.AddFilter<OriginHubFilter>();
});
builder.Services.RemoveAll<IChannelNotifier>();
builder.Services.AddSingleton<IChannelNotifier, CoalescingChannelNotifier>();
builder.Services.RemoveAll<IAuthStateChanged>();
builder.Services.AddSingleton<AuthStateChangedService>();
builder.Services.AddSingleton<IAuthStateChanged>(sp => sp.GetRequiredService<AuthStateChangedService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<AuthStateChangedService>());

var app = builder.Build();

if (args.Contains("seed-operator", StringComparer.OrdinalIgnoreCase))
{
    await using var scope = app.Services.CreateAsyncScope();
    var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
    var email = GetArg(args, "--email") ?? Environment.GetEnvironmentVariable("CHANNELED_OPERATOR_EMAIL")
        ?? throw new InvalidOperationException("Provide --email or CHANNELED_OPERATOR_EMAIL");
    var password = GetArg(args, "--password") ?? Environment.GetEnvironmentVariable("CHANNELED_OPERATOR_PASSWORD")
        ?? throw new InvalidOperationException("Provide --password or CHANNELED_OPERATOR_PASSWORD");
    var name = GetArg(args, "--name") ?? "Operator";

    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.MigrateAsync();

    var result = await auth.SeedOperatorAsync(new SeedOperatorRequest(email, name, password));
    if (!result.Succeeded)
    {
        Console.Error.WriteLine($"{result.Code}: {result.Message}");
        return 1;
    }

    Console.WriteLine($"Seeded operator {result.Value!.Email} ({result.Value.Id})");
    return 0;
}

if (args.Contains("ensure-user", StringComparer.OrdinalIgnoreCase))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.MigrateAsync();
    var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
    var email = GetArg(args, "--email") ?? throw new InvalidOperationException("Provide --email");
    var password = GetArg(args, "--password") ?? throw new InvalidOperationException("Provide --password");
    var name = GetArg(args, "--name") ?? "User";
    var admin = args.Contains("--admin", StringComparer.OrdinalIgnoreCase);

    var user = await users.FindByEmailAsync(email);
    if (user is null)
    {
        user = new AppUser
        {
            Id = UuidV7.NewGuid(),
            Email = email,
            UserName = email,
            DisplayName = name,
            UsernameSlug = TruncateUsernameSlug(email.Split('@')[0].Replace(".", "")),
            EmailConfirmed = true,
            IsPlatformAdmin = admin,
            Status = "active",
        };
        var created = await users.CreateAsync(user, password);
        if (!created.Succeeded)
        {
            Console.Error.WriteLine(string.Join("; ", created.Errors.Select(e => e.Description)));
            return 1;
        }

        Console.WriteLine($"Created user {email} admin={admin}");
        return 0;
    }

    user.DisplayName = name;
    user.IsPlatformAdmin = admin || user.IsPlatformAdmin;
    user.Status = "active";
    await users.UpdateAsync(user);
    var token = await users.GeneratePasswordResetTokenAsync(user);
    var reset = await users.ResetPasswordAsync(user, token, password);
    if (!reset.Succeeded)
    {
        Console.Error.WriteLine(string.Join("; ", reset.Errors.Select(e => e.Description)));
        return 1;
    }

    Console.WriteLine($"Updated user {email} admin={user.IsPlatformAdmin}");
    return 0;
}

if (args.Contains("backup", StringComparer.OrdinalIgnoreCase))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.MigrateAsync();
    var kind = GetArg(args, "--kind") ?? "ondemand";
    var backups = scope.ServiceProvider.GetRequiredService<IBackupService>();
    var info = await backups.CreateAsync(kind);
    Console.WriteLine($"Backup {info.Id} state={info.State} objects={info.ObjectCount} bytes={info.TotalBytes} dir={info.Directory}");
    return info.State is "verified" or "restored" ? 0 : 1;
}

if (args.Contains("restore", StringComparer.OrdinalIgnoreCase))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.MigrateAsync();
    var idRaw = GetArg(args, "--id") ?? throw new InvalidOperationException("Provide --id <backup-guid>");
    if (!Guid.TryParse(idRaw, out var backupId))
    {
        throw new InvalidOperationException("Invalid --id");
    }

    var backups = scope.ServiceProvider.GetRequiredService<IBackupService>();
    var info = await backups.RestoreAsync(backupId);
    Console.WriteLine($"Restored backup {info.Id} state={info.State}");
    return 0;
}

if (args.Contains("reconcile-store", StringComparer.OrdinalIgnoreCase))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.MigrateAsync();
    var reconcile = scope.ServiceProvider.GetRequiredService<IStoreReconciliationService>();
    var report = await reconcile.ReconcileAsync();
    Console.WriteLine($"db={report.DatabaseKeys} store={report.StoreKeys} missing={report.MissingInStore.Count} orphans={report.OrphansInStore.Count} checksum={report.ChecksumMismatches.Count}");
    return report.IsClean ? 0 : 2;
}

if (args.Contains("restore-drill", StringComparer.OrdinalIgnoreCase))
{
    return await RunRestoreDrillAsync(app.Services);
}

if (args.Contains("seed-bench-files", StringComparer.OrdinalIgnoreCase))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.MigrateAsync();

    var count = int.TryParse(GetArg(args, "--count"), out var n) && n > 0
        ? n
        : app.Configuration.GetValue("SearchBench:DefaultSeedCount", 1_000_000);
    var email = GetArg(args, "--email") ?? "bench@example.com";
    var password = GetArg(args, "--password") ?? "BenchPassword1!";

    var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
    var user = await users.FindByEmailAsync(email);
    if (user is null)
    {
        user = new AppUser
        {
            Id = UuidV7.NewGuid(),
            Email = email,
            UserName = email,
            DisplayName = "Bench",
            UsernameSlug = TruncateUsernameSlug(email.Split('@')[0].Replace(".", "")),
            EmailConfirmed = true,
            Status = "active",
        };
        var created = await users.CreateAsync(user, password);
        if (!created.Succeeded)
        {
            Console.Error.WriteLine(string.Join("; ", created.Errors.Select(e => e.Description)));
            return 1;
        }
    }

    var seeder = new BenchFileSeeder(db);
    var result = await seeder.SeedAsync(user.Id, count);
    Console.WriteLine($"Seeded {result.Inserted} files into team '{result.TeamSlug}' channel general for {email}");
    return 0;
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var migrateOnStart = app.Configuration.GetValue("MigrateOnStartup", app.Environment.IsDevelopment());
    if (migrateOnStart)
    {
        await db.Database.MigrateAsync();
    }
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
});

app.Use(async (context, next) =>
{
    // Token links must not leak via Referer (fragment still preferred for tokens).
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    await next();
});

app.Use(async (context, next) =>
{
    var correlationId = context.Request.Headers["X-Correlation-ID"].FirstOrDefault();
    if (string.IsNullOrWhiteSpace(correlationId))
    {
        correlationId = Guid.NewGuid().ToString("N");
    }

    context.Response.Headers["X-Correlation-ID"] = correlationId;
    context.Items["CorrelationId"] = correlationId;
    Activity.Current?.SetTag("correlation.id", correlationId);
    await next();
});

app.Use(async (context, next) =>
{
    var maintenance = context.RequestServices.GetRequiredService<IMaintenanceMode>();
    if (maintenance.IsEnabled)
    {
        var path = context.Request.Path.Value ?? "";
        var allowed = path.Equals("/health", StringComparison.OrdinalIgnoreCase)
                      || path.Equals("/api/v1/health", StringComparison.OrdinalIgnoreCase)
                      || path.Equals("/metrics", StringComparison.OrdinalIgnoreCase)
                      || path.StartsWith("/api/v1/admin/backups", StringComparison.OrdinalIgnoreCase);
        if (!allowed)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(new
            {
                type = "https://channeled.local/errors/maintenance",
                title = "maintenance",
                status = 503,
                detail = "Channeled is in maintenance mode.",
                code = "maintenance",
            });
            return;
        }
    }

    await next();
});

var meter = new Meter("Channeled.Api", "1.0.0");
var requestCounter = meter.CreateCounter<long>("channeled_requests_total");
app.Use(async (context, next) =>
{
    requestCounter.Add(1);
    await next();
});

// Authentication runs before the limiter so per-user policies (chat posts) can partition by user id.
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.Use(async (context, next) =>
{
    if (HttpMethods.IsPost(context.Request.Method)
        || HttpMethods.IsPut(context.Request.Method)
        || HttpMethods.IsPatch(context.Request.Method)
        || HttpMethods.IsDelete(context.Request.Method))
    {
        // Login/accept/forgot/reset/csrf bootstrap are anonymous and CSRF-exempt for first cookie issuance.
        var path = context.Request.Path.Value ?? "";
        var exempt = path.StartsWith("/api/v1/auth/login", StringComparison.OrdinalIgnoreCase)
                     || path.StartsWith("/api/v1/auth/register", StringComparison.OrdinalIgnoreCase)
                     || path.StartsWith("/api/v1/auth/invitations/accept", StringComparison.OrdinalIgnoreCase)
                     || path.StartsWith("/api/v1/auth/forgot-password", StringComparison.OrdinalIgnoreCase)
                     || path.StartsWith("/api/v1/auth/reset-password", StringComparison.OrdinalIgnoreCase)
                     || path.StartsWith("/api/v1/auth/csrf", StringComparison.OrdinalIgnoreCase)
                     || path.StartsWith("/api/v1/account/email-change/confirm", StringComparison.OrdinalIgnoreCase)
                     || path.StartsWith("/api/v1/account/email-change/cancel", StringComparison.OrdinalIgnoreCase)
                     || path.StartsWith("/api/v1/account/email-change/sign-out-all", StringComparison.OrdinalIgnoreCase)
                     || path.StartsWith("/api/v1/account/email-change/password", StringComparison.OrdinalIgnoreCase)
                     // Anonymous public share claim: no cookie session, so there is no CSRF surface.
                     || path.StartsWith("/api/v1/public/", StringComparison.OrdinalIgnoreCase);

        if (!exempt)
        {
            var antiforgery = context.RequestServices.GetRequiredService<Microsoft.AspNetCore.Antiforgery.IAntiforgery>();
            try
            {
                await antiforgery.ValidateRequestAsync(context);
            }
            catch (Microsoft.AspNetCore.Antiforgery.AntiforgeryValidationException)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new
                {
                    type = "https://channeled.local/errors/csrf_rejected",
                    title = "csrf_rejected",
                    status = 400,
                    detail = "CSRF validation failed.",
                    code = "csrf_rejected",
                });
                return;
            }
        }
    }

    await next();
});

app.MapGet("/health", async Task<IResult> (ISystemHealthService health, CancellationToken ct) =>
    {
        var report = await health.CheckAsync(ct);
        return Results.Json(report, statusCode: report.Status == "ok" ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
    })
    .WithName("Health")
    .WithTags("Health")
    .AllowAnonymous();

app.MapGet("/api/v1/health", async Task<IResult> (ISystemHealthService health, CancellationToken ct) =>
    {
        var report = await health.CheckAsync(ct);
        return Results.Json(report, statusCode: report.Status == "ok" ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
    })
    .WithName("ApiHealth")
    .WithTags("Health")
    .AllowAnonymous();

app.MapGet("/metrics", async Task<IResult> (ISystemHealthService healthSvc, CancellationToken ct) =>
    {
        var report = await healthSvc.CheckAsync(ct);
        var body = $"""
            # HELP channeled_up 1 if process is serving
            # TYPE channeled_up gauge
            channeled_up 1
            # HELP channeled_db_up 1 if database is reachable
            # TYPE channeled_db_up gauge
            channeled_db_up {(report.Database ? 1 : 0)}
            # HELP channeled_store_up 1 if object store is reachable
            # TYPE channeled_store_up gauge
            channeled_store_up {(report.Store ? 1 : 0)}
            # HELP channeled_disk_free_bytes Free bytes on store volume
            # TYPE channeled_disk_free_bytes gauge
            channeled_disk_free_bytes {report.DiskFreeBytes}
            # HELP channeled_jobs_pending Pending or running jobs
            # TYPE channeled_jobs_pending gauge
            channeled_jobs_pending {report.PendingJobs}
            """;
        return Results.Text(body, "text/plain; version=0.0.4");
    })
    .WithName("Metrics")
    .WithTags("Health")
    .AllowAnonymous();

// Startup guards for account/mail configuration.
{
    var accountOpts = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<AccountOptions>>().Value;
    if (accountOpts.SessionCacheTtlSeconds > 5)
    {
        throw new InvalidOperationException("Account:SessionCacheTtlSeconds must be 5 or less.");
    }

    // Self-service availability is computed at runtime (SMTP sender ∧ smtp_verified_at ∧ fingerprint).
    // Logging sender + Auto is fine: IsAvailableAsync stays false; no-mail prod still starts.
    _ = app.Services.GetRequiredService<IEmailSenderMarker>();
    _ = accountOpts.SelfServiceMail;
}

app.MapAuthEndpoints();
app.MapUserEndpoints();
app.MapAccountEndpoints();
app.MapAccountMailEndpoints();
app.MapTeamEndpoints();
app.MapFileEndpoints();
app.MapChatEndpoints();
app.MapNotificationEndpoints();
app.MapShareLinkEndpoints();
app.MapPersonalEndpoints();
app.MapSearchEndpoints();
app.MapAdminEndpoints();
app.MapHub<ChannelHub>("/hubs/channel", options =>
{
    options.Transports = HttpTransportType.WebSockets;
});

app.Run();
return 0;

static string? GetArg(string[] args, string name)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
        {
            return args[i + 1];
        }
    }

    return null;
}

static string TruncateUsernameSlug(string slug) =>
    slug.Length <= 30 ? slug : slug[..30];

/// <summary>Stable partition key from JSON body <c>token</c> (or "none"). Enables per-IP+per-token limits.</summary>
static string PeekJsonTokenKey(HttpRequest request)
{
    try
    {
        request.EnableBuffering();
        if (request.ContentLength is null or <= 0 or > 16_384)
        {
            return "none";
        }

        var buffer = new byte[(int)request.ContentLength.Value];
        var read = request.Body.Read(buffer, 0, buffer.Length);
        request.Body.Position = 0;
        if (read <= 0)
        {
            return "none";
        }

        using var doc = JsonDocument.Parse(buffer.AsMemory(0, read));
        if (!doc.RootElement.TryGetProperty("token", out var tokenProp))
        {
            return "none";
        }

        var token = tokenProp.GetString() ?? "";
        if (token.Length == 0)
        {
            return "none";
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant();
    }
    catch
    {
        try
        {
            request.Body.Position = 0;
        }
        catch
        {
            // ignore
        }

        return "none";
    }
}

static async Task<int> RunRestoreDrillAsync(IServiceProvider root)
{
    await using var scope = root.CreateAsyncScope();
    var sp = scope.ServiceProvider;
    var db = sp.GetRequiredService<ApplicationDbContext>();
    await db.Database.MigrateAsync();

    var store = sp.GetRequiredService<Channeled.Application.Abstractions.Storage.IFileStore>();
    var backups = sp.GetRequiredService<IBackupService>();
    var reconcile = sp.GetRequiredService<IStoreReconciliationService>();
    var clock = sp.GetRequiredService<Channeled.Application.Abstractions.Time.IClock>();

    // Seed one known object + file row
    var key = Channeled.Domain.Storage.ObjectKey.ForFile(UuidV7.NewGuid());
    var payload = System.Text.Encoding.UTF8.GetBytes("restore-drill-" + Guid.NewGuid().ToString("N"));
    string sha;
    await using (var input = new MemoryStream(payload))
    {
        await store.PutAsync(key, input, "text/plain");
    }

    await using (var read = await store.OpenReadAsync(key))
    {
        sha = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(read)).ToLowerInvariant();
    }

    var userId = UuidV7.NewGuid();
    var users = sp.GetRequiredService<UserManager<AppUser>>();
    var user = new AppUser
    {
        Id = userId,
        Email = $"drill-{userId:N}@example.com",
        UserName = $"drill-{userId:N}@example.com",
        DisplayName = "Drill",
        UsernameSlug = $"drill{userId:N}"[..30],
        EmailConfirmed = true,
        Status = "active",
    };
    var created = await users.CreateAsync(user, "DrillPassword1!");
    if (!created.Succeeded)
    {
        Console.Error.WriteLine(string.Join("; ", created.Errors.Select(e => e.Description)));
        return 1;
    }

    db.Files.Add(new Channeled.Infrastructure.Files.FileEntity
    {
        Id = UuidV7.NewGuid(),
        OwnerUserId = userId,
        Name = "drill.txt",
        ContentType = "text/plain",
        SizeBytes = payload.Length,
        Sha256 = sha,
        StorageKey = key,
        UploadedBy = userId,
        CreatedAt = clock.UtcNow,
        UpdatedAt = clock.UtcNow,
    });
    await db.SaveChangesAsync();

    var package = await backups.CreateAsync("ondemand");
    if (package.State != "verified")
    {
        Console.Error.WriteLine($"Backup not verified: {package.State} {package.Error}");
        return 1;
    }

    // Simulate catastrophic object-store loss before guided restore.
    foreach (var k in await store.ListObjectKeysAsync())
    {
        await store.DeleteAsync(k);
    }

    var restored = await backups.RestoreAsync(package.Id);
    var report = await reconcile.ReconcileAsync();
    Console.WriteLine($"restore-drill backup={package.Id} state={restored.State} clean={report.IsClean} missing={report.MissingInStore.Count} orphans={report.OrphansInStore.Count} checksum={report.ChecksumMismatches.Count}");
    if (!report.IsClean)
    {
        return 2;
    }

    await using var verify = await store.OpenReadAsync(key);
    using var buf = new MemoryStream();
    await verify.CopyToAsync(buf);
    if (!buf.ToArray().SequenceEqual(payload))
    {
        Console.Error.WriteLine("Restored object bytes mismatch");
        return 3;
    }

    Console.WriteLine("restore-drill OK");
    return 0;
}

public partial class Program;
