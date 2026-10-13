using System.Security.Claims;
using Channeled.Application.Abstractions.Results;
using Channeled.Application.Identity;
using Channeled.Application.PublicLinks;
using Channeled.Domain.Errors;
using Channeled.Infrastructure.PublicLinks;
using Microsoft.Net.Http.Headers;

namespace Channeled.Api.PublicLinks;

public static class ShareLinkEndpoints
{
    public const string AnonRateLimitPolicy = "public-share";

    public static IEndpointRouteBuilder MapShareLinkEndpoints(this IEndpointRouteBuilder app)
    {
        // ---- authenticated: create / list / revoke -------------------------------------------------
        var channel = app.MapGroup("/api/v1/teams/{slug}/channels/{channelName}")
            .WithTags("ShareLinks")
            .RequireAuthorization();

        channel.MapPost("/items/{fileId:guid}/share-links", async Task<IResult> (
            string slug,
            string channelName,
            Guid fileId,
            CreateShareLinkRequest? body,
            IShareLinkService links,
            ClaimsPrincipal principal,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            return ToHttp(await links.CreateAsync(
                userId, slug, channelName, fileId, body ?? new CreateShareLinkRequest(null, null), Ctx(http), ct));
        });

        channel.MapGet("/share-links", async Task<IResult> (
            string slug,
            string channelName,
            Guid? fileId,
            IShareLinkService links,
            ClaimsPrincipal principal,
            CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            return ToHttp(await links.ListAsync(userId, slug, channelName, fileId, ct));
        });

        app.MapDelete("/api/v1/teams/{slug}/share-links/{linkId:guid}", async Task<IResult> (
                string slug,
                Guid linkId,
                IShareLinkService links,
                ClaimsPrincipal principal,
                HttpContext http,
                CancellationToken ct) =>
            {
                if (!TryUser(principal, out var userId))
                {
                    return Unauthorized();
                }

                return ToHttp(await links.RevokeAsync(userId, slug, linkId, Ctx(http), ct));
            })
            .WithTags("ShareLinks")
            .RequireAuthorization();

        // ---- platform admin ------------------------------------------------------------------------
        var admin = app.MapGroup("/api/v1/admin/share-links").WithTags("ShareLinks").RequireAuthorization();

        admin.MapGet("/", async Task<IResult> (
            bool? activeOnly,
            IShareLinkService links,
            ClaimsPrincipal principal,
            CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            return ToHttp(await links.AdminListAsync(userId, activeOnly ?? false, ct));
        });

        admin.MapPost("/revoke-all", async Task<IResult> (
            IShareLinkService links,
            ClaimsPrincipal principal,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            return ToHttp(await links.AdminRevokeAllAsync(userId, Ctx(http), ct));
        });

        // ---- anonymous (no cookie, no CSRF; per-IP rate limited) -----------------------------------
        var pub = app.MapGroup("/api/v1/public/shares")
            .WithTags("PublicShares")
            .AllowAnonymous()
            .RequireRateLimiting(AnonRateLimitPolicy);

        pub.MapGet("/{token}", async Task<IResult> (string token, IShareLinkService links, CancellationToken ct) =>
            ToHttp(await links.GetPublicInfoAsync(token, ct)));

        pub.MapPost("/{token}/claim", async Task<IResult> (
            string token,
            ClaimShareRequest? body,
            IShareLinkService links,
            HttpContext http,
            CancellationToken ct) =>
        {
            var key = http.Request.Headers["Idempotency-Key"].FirstOrDefault() ?? body?.IdempotencyKey;
            return ToHttp(await links.ClaimAsync(token, key, Ctx(http), ct));
        });

        pub.MapGet("/{token}/download", async Task<IResult> (
            string token,
            string? ticket,
            IShareLinkService links,
            HttpContext http,
            CancellationToken ct) =>
        {
            long? rangeStart = null;
            long? rangeEnd = null;
            if (http.Request.Headers.TryGetValue(HeaderNames.Range, out var rangeHeader)
                && RangeHeaderValue.TryParse(rangeHeader.ToString(), out var range)
                && range.Ranges.Count == 1)
            {
                var r = range.Ranges.First();
                rangeStart = r.From;
                rangeEnd = r.To;
            }

            var ip = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var result = await links.OpenTicketStreamAsync(token, ticket ?? "", rangeStart, rangeEnd, ip, ct);
            if (!result.Succeeded || result.Value is null)
            {
                return ToHttp(result);
            }

            var d = result.Value;
            var response = http.Response;
            response.Headers.CacheControl = "no-store";
            response.Headers.Append("X-Content-Type-Options", "nosniff");
            response.Headers.Append("Content-Security-Policy", "default-src 'none'; sandbox");
            response.Headers.AcceptRanges = "bytes";
            response.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
            {
                FileNameStar = d.FileName,
            }.ToString();
            response.ContentType = "application/octet-stream";

            var length = d.SizeBytes == 0 ? 0 : d.RangeEndInclusive - d.RangeStart + 1;
            if (d.IsPartial && d.SizeBytes > 0)
            {
                response.StatusCode = StatusCodes.Status206PartialContent;
                response.Headers.ContentRange = $"bytes {d.RangeStart}-{d.RangeEndInclusive}/{d.SizeBytes}";
            }

            response.ContentLength = length;

            await using (d.Content)
            {
                try
                {
                    await d.Content.CopyToAsync(response.Body, ct);
                }
                catch (ShareStreamAbortedException)
                {
                    // Revoked / trashed / archived / disabled / byte cap mid-stream: cut the connection so the
                    // client sees a failed (not silently truncated) download.
                    http.Abort();
                }
            }

            return Results.Empty;
        });

        return app;
    }

    private static bool TryUser(ClaimsPrincipal principal, out Guid userId) =>
        Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    private static RequestContext Ctx(HttpContext http) =>
        new(http.Connection.RemoteIpAddress?.ToString() ?? "", http.Request.Headers.UserAgent.ToString());

    private static IResult Unauthorized() =>
        Problem(ErrorCodes.Unauthorized, "Not authenticated.", StatusCodes.Status401Unauthorized);

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
        ErrorCodes.Forbidden or ErrorCodes.ShareTicketInvalid => StatusCodes.Status403Forbidden,
        ErrorCodes.NotFound => StatusCodes.Status404NotFound,
        ErrorCodes.Conflict or ErrorCodes.NameConflict or ErrorCodes.ShareLinkLimit => StatusCodes.Status409Conflict,
        ErrorCodes.FileTooLarge => StatusCodes.Status413PayloadTooLarge,
        ErrorCodes.RateLimited => StatusCodes.Status429TooManyRequests,
        ErrorCodes.RangeNotSatisfiable => StatusCodes.Status416RangeNotSatisfiable,
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
