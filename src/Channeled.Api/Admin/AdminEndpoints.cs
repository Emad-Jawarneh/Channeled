using System.Security.Claims;
using Channeled.Application.Abstractions.Results;
using Channeled.Application.Admin;
using Channeled.Application.Identity;
using Channeled.Domain.Audit;
using Channeled.Domain.Errors;

namespace Channeled.Api.Admin;

public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/v1/admin").WithTags("Admin").RequireAuthorization();

        admin.MapGet("/dashboard", async Task<IResult> (IAdminService svc, ClaimsPrincipal p, CancellationToken ct) =>
            await RequireUser(p, async userId => ToHttp(await svc.GetDashboardAsync(userId, ct))));

        admin.MapGet("/health", async Task<IResult> (IAdminService svc, ClaimsPrincipal p, CancellationToken ct) =>
            await RequireUser(p, async userId => ToHttp(await svc.GetHealthAsync(userId, ct))));

        admin.MapGet("/users", async Task<IResult> (IAdminService svc, ClaimsPrincipal p, CancellationToken ct) =>
            await RequireUser(p, async userId => ToHttp(await svc.ListUsersAsync(userId, ct))));

        admin.MapGet("/users/{id:guid}", async Task<IResult> (Guid id, IAdminService svc, ClaimsPrincipal p, CancellationToken ct) =>
            await RequireUser(p, async userId => ToHttp(await svc.GetUserAsync(userId, id, ct))));

        admin.MapPost("/users", async Task<IResult> (
            CreateAdminUserRequest body, IAdminService svc, ClaimsPrincipal p, HttpContext http, CancellationToken ct) =>
            await RequireUser(p, async userId => ToHttp(await svc.CreateUserAsync(userId, body, Ctx(http), ct))));

        admin.MapPatch("/users/{id:guid}", async Task<IResult> (
            Guid id, UpdateAdminUserRequest body, IAdminService svc, ClaimsPrincipal p, HttpContext http, CancellationToken ct) =>
            await RequireUser(p, async userId => ToHttp(await svc.UpdateUserAsync(userId, id, body, Ctx(http), ct))));

        admin.MapPost("/users/{id:guid}/disable", async Task<IResult> (
            Guid id, IAdminService svc, ClaimsPrincipal p, HttpContext http, CancellationToken ct) =>
            await RequireUser(p, async userId => ToHttp(await svc.DisableUserAsync(userId, id, Ctx(http), ct))));

        admin.MapDelete("/users/{id:guid}", async Task<IResult> (
            Guid id, IAdminService svc, ClaimsPrincipal p, HttpContext http, CancellationToken ct) =>
            await RequireUser(p, async userId => ToHttp(await svc.DeleteUserAsync(userId, id, Ctx(http), ct))));

        admin.MapPost("/users/{id:guid}/reset-password", async Task<IResult> (
            Guid id, ResetUserPasswordRequest body, IAdminService svc, ClaimsPrincipal p, HttpContext http, CancellationToken ct) =>
            await RequireUser(p, async userId => ToHttp(await svc.ResetUserPasswordAsync(userId, id, body, Ctx(http), ct))));

        admin.MapGet("/teams", async Task<IResult> (IAdminService svc, ClaimsPrincipal p, CancellationToken ct) =>
            await RequireUser(p, async userId => ToHttp(await svc.ListTeamsAsync(userId, ct))));

        admin.MapPatch("/teams/{id:guid}/quota", async Task<IResult> (
            Guid id, UpdateTeamQuotaRequest body, IAdminService svc, ClaimsPrincipal p, HttpContext http, CancellationToken ct) =>
            await RequireUser(p, async userId => ToHttp(await svc.UpdateTeamQuotaAsync(userId, id, body, Ctx(http), ct))));

        admin.MapGet("/audit", async Task<IResult> (
            IAdminService svc,
            ClaimsPrincipal p,
            AuditEventType? eventType,
            Guid? actorId,
            Guid? teamId,
            string? targetType,
            DateTimeOffset? from,
            DateTimeOffset? to,
            string? cursor,
            int? limit,
            CancellationToken ct) =>
            await RequireUser(p, async userId => ToHttp(await svc.ListAuditAsync(
                userId,
                new AuditListQuery(eventType, actorId, teamId, targetType, from, to, cursor, limit ?? 50),
                ct))));

        admin.MapGet("/audit/export", async Task<IResult> (
            IAdminService svc,
            ClaimsPrincipal p,
            string? format,
            AuditEventType? eventType,
            Guid? actorId,
            Guid? teamId,
            string? targetType,
            DateTimeOffset? from,
            DateTimeOffset? to,
            CancellationToken ct) =>
            await RequireUser(p, async userId =>
            {
                var result = await svc.ExportAuditAsync(
                    userId,
                    new AuditListQuery(eventType, actorId, teamId, targetType, from, to, null, 200),
                    format ?? "json",
                    ct);
                if (!result.Succeeded || result.Value is null)
                {
                    return ToHttp(result);
                }

                var isCsv = string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase);
                return Results.Text(
                    result.Value,
                    isCsv ? "text/csv" : "application/json");
            }));

        admin.MapGet("/backups", async Task<IResult> (IAdminService svc, ClaimsPrincipal p, CancellationToken ct) =>
            await RequireUser(p, async userId => ToHttp(await svc.ListBackupsAsync(userId, ct))));

        admin.MapPost("/backups/run", async Task<IResult> (
            IAdminService svc, ClaimsPrincipal p, HttpContext http, CancellationToken ct) =>
            await RequireUser(p, async userId => ToHttp(await svc.RunBackupAsync(userId, Ctx(http), ct))));

        admin.MapPost("/backups/{id:guid}/restore", async Task<IResult> (
            Guid id, IAdminService svc, ClaimsPrincipal p, HttpContext http, CancellationToken ct) =>
            await RequireUser(p, async userId => ToHttp(await svc.RecordBackupRestoreAsync(userId, id, Ctx(http), ct))));

        admin.MapPost("/jobs/store-reconcile", async Task<IResult> (
            Channeled.Application.Abstractions.Operations.IStoreReconciliationService reconcile,
            IAuthService auth,
            ClaimsPrincipal p,
            CancellationToken ct) =>
        {
            if (!TryUser(p, out var userId))
            {
                return Problem(ErrorCodes.Unauthorized, "Not authenticated.", StatusCodes.Status401Unauthorized);
            }

            var me = await auth.GetCurrentUserAsync(userId, ct);
            if (!me.Succeeded || me.Value is null || !me.Value.IsPlatformAdmin)
            {
                return Results.NotFound();
            }

            var report = await reconcile.ReconcileAsync(ct);
            return Results.Ok(report);
        });

        admin.MapPost("/config", async Task<IResult> (
            ConfigChangeBody body, IAdminService svc, ClaimsPrincipal p, HttpContext http, CancellationToken ct) =>
            await RequireUser(p, async userId => ToHttp(await svc.RecordConfigChangeAsync(userId, body.Key, body.Value, Ctx(http), ct))));

        admin.MapPost("/files/{id:guid}/purge", async Task<IResult> (
            Guid id, IAdminService svc, ClaimsPrincipal p, HttpContext http, CancellationToken ct) =>
            await RequireUser(p, async userId => ToHttp(await svc.PurgeFileAsync(userId, id, Ctx(http), ct))));

        admin.MapPost("/files/{id:guid}/download-audit", async Task<IResult> (
            Guid id, IAdminService svc, ClaimsPrincipal p, HttpContext http, CancellationToken ct) =>
            await RequireUser(p, async userId => ToHttp(await svc.RecordDownloadAsync(userId, id, Ctx(http), ct))));

        return app;
    }

    private sealed record ConfigChangeBody(string Key, string Value);

    private static async Task<IResult> RequireUser(ClaimsPrincipal principal, Func<Guid, Task<IResult>> next)
    {
        if (!TryUser(principal, out var userId))
        {
            return Problem(ErrorCodes.Unauthorized, "Not authenticated.", StatusCodes.Status401Unauthorized);
        }

        return await next(userId);
    }

    private static bool TryUser(ClaimsPrincipal principal, out Guid userId)
    {
        var raw = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out userId);
    }

    private static RequestContext Ctx(HttpContext http) =>
        new(http.Connection.RemoteIpAddress?.ToString() ?? "", http.Request.Headers.UserAgent.ToString());

    private static IResult ToHttp(AppResult result) =>
        result.Succeeded
            ? Results.Ok(new { status = "ok" })
            : Problem(result.Code ?? ErrorCodes.Validation, result.Message ?? "Request failed.", MapStatus(result.Code));

    private static IResult ToHttp<T>(AppResult<T> result) =>
        result.Succeeded
            ? Results.Ok(result.Value)
            : Problem(result.Code ?? ErrorCodes.Validation, result.Message ?? "Request failed.", MapStatus(result.Code));

    private static int MapStatus(string? code) => code switch
    {
        ErrorCodes.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorCodes.NotFound => StatusCodes.Status404NotFound,
        ErrorCodes.Conflict => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status400BadRequest,
    };

    private static IResult Problem(string code, string detail, int status) =>
        Results.Json(
            new
            {
                type = $"https://channeled.local/errors/{code}",
                title = code,
                status,
                detail,
                code,
            },
            statusCode: status);
}
