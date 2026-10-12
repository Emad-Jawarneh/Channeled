using System.Threading.RateLimiting;
using Channeled.Api.Auth;
using Channeled.Application.Identity;
using Channeled.Infrastructure;
using Channeled.Infrastructure.Persistence;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

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
    options.AddFixedWindowLimiter("auth", limiter =>
    {
        limiter.PermitLimit = 20;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
    });
});

builder.Services.AddAuthorization();

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

app.UseRateLimiter();
app.UseAuthentication();
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
                     || path.StartsWith("/api/v1/auth/invitations/accept", StringComparison.OrdinalIgnoreCase)
                     || path.StartsWith("/api/v1/auth/forgot-password", StringComparison.OrdinalIgnoreCase)
                     || path.StartsWith("/api/v1/auth/reset-password", StringComparison.OrdinalIgnoreCase)
                     || path.StartsWith("/api/v1/auth/csrf", StringComparison.OrdinalIgnoreCase);

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

app.MapGet("/health", (Microsoft.Extensions.Options.IOptions<Channeled.Application.Abstractions.Storage.FileStoreOptions> storeOptions) =>
        Results.Ok(new
        {
            status = "ok",
            store = storeOptions.Value.Kind.ToString(),
        }))
    .WithName("Health")
    .WithTags("Health")
    .AllowAnonymous();

app.MapGet("/api/v1/health", () => Results.Ok(new { status = "ok" }))
    .WithName("ApiHealth")
    .WithTags("Health")
    .AllowAnonymous();

app.MapAuthEndpoints();

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

public partial class Program;
