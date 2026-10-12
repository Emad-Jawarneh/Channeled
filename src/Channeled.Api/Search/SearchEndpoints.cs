using System.Security.Claims;
using Channeled.Application.Abstractions.Results;
using Channeled.Application.Identity;
using Channeled.Application.Search;
using Channeled.Domain.Errors;
using Channeled.Domain.Search;

namespace Channeled.Api.Search;

public static class SearchEndpoints
{
    public static IEndpointRouteBuilder MapSearchEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/search", async Task<IResult> (
            ISearchService search,
            ClaimsPrincipal principal,
            string q,
            string scope,
            string? teamSlug,
            string? channelName,
            string? type,
            Guid? uploaderId,
            string? cursor,
            int? limit,
            CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            if (!Enum.TryParse<SearchScope>(scope, ignoreCase: true, out var parsedScope))
            {
                return Problem(ErrorCodes.Validation, "scope must be channel, team, or personal.", StatusCodes.Status400BadRequest);
            }

            return ToHttp(await search.SearchAsync(
                userId,
                new SearchQuery(q, parsedScope, teamSlug, channelName, type, uploaderId, cursor, limit ?? 50),
                ct));
        }).WithTags("Search").RequireAuthorization();

        app.MapGet("/api/v1/teams/{slug}/storage", async Task<IResult> (
            string slug,
            IUsageService usage,
            ClaimsPrincipal principal,
            CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            return ToHttp(await usage.GetTeamStorageAsync(userId, slug, ct));
        }).WithTags("Teams").RequireAuthorization();

        app.MapPost("/api/v1/admin/jobs/quota-reconcile", async Task<IResult> (
            IQuotaReconciliationService reconcile,
            IAuthService auth,
            ClaimsPrincipal principal,
            CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            var me = await auth.GetCurrentUserAsync(userId, ct);
            if (!me.Succeeded || me.Value is null || !me.Value.IsPlatformAdmin)
            {
                return Problem(ErrorCodes.NotFound, "Not found.", StatusCodes.Status404NotFound);
            }

            var result = await reconcile.ReconcileAsync(ct);
            return Results.Ok(result);
        }).WithTags("Admin").RequireAuthorization();

        return app;
    }

    private static bool TryUser(ClaimsPrincipal principal, out Guid userId)
    {
        var raw = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out userId);
    }

    private static IResult Unauthorized() =>
        Problem(ErrorCodes.Unauthorized, "Not authenticated.", StatusCodes.Status401Unauthorized);

    private static IResult ToHttp<T>(AppResult<T> result) =>
        result.Succeeded
            ? Results.Ok(result.Value)
            : Problem(result.Code ?? ErrorCodes.Validation, result.Message ?? "Request failed.", MapStatus(result.Code));

    private static int MapStatus(string? code) => code switch
    {
        ErrorCodes.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorCodes.NotFound => StatusCodes.Status404NotFound,
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
