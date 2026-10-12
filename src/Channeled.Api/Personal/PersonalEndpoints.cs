using System.Security.Claims;
using Channeled.Application.Abstractions.Results;
using Channeled.Application.Files;
using Channeled.Application.Identity;
using Channeled.Application.Personal;
using Channeled.Domain.Errors;
using Channeled.Infrastructure.Files;
using Microsoft.Net.Http.Headers;

namespace Channeled.Api.Personal;

public static class PersonalEndpoints
{
    public static IEndpointRouteBuilder MapPersonalEndpoints(this IEndpointRouteBuilder app)
    {
        var me = app.MapGroup("/api/v1/me").WithTags("Personal").RequireAuthorization();

        me.MapGet("/storage", async Task<IResult> (IPersonalService personal, ClaimsPrincipal principal, CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            return ToHttp(await personal.GetStorageAsync(userId, ct));
        });

        var files = me.MapGroup("/files");

        files.MapPost("/folders", async Task<IResult> (
            CreateFolderRequest body,
            IPersonalService personal,
            ClaimsPrincipal principal,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            return ToHttp(await personal.CreateFolderAsync(userId, body, Ctx(http), ct));
        });

        files.MapGet("/items", async Task<IResult> (
            IPersonalService personal,
            ClaimsPrincipal principal,
            Guid? folderId,
            string? cursor,
            int? limit,
            string? sort,
            string? type,
            bool? trash,
            CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            return ToHttp(await personal.ListItemsAsync(
                userId,
                new ListItemsQuery(folderId, cursor, limit ?? 50, sort, type, null, trash ?? false),
                ct));
        });

        files.MapGet("/recent", async Task<IResult> (
            IPersonalService personal,
            ClaimsPrincipal principal,
            string? cursor,
            int? limit,
            CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            return ToHttp(await personal.ListRecentAsync(userId, cursor, limit ?? 50, ct));
        });

        files.MapGet("/starred", async Task<IResult> (
            IPersonalService personal,
            ClaimsPrincipal principal,
            string? cursor,
            int? limit,
            CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            return ToHttp(await personal.ListStarredAsync(userId, cursor, limit ?? 50, ct));
        });

        files.MapGet("/items/{itemId:guid}", async Task<IResult> (
            Guid itemId,
            IPersonalService personal,
            ClaimsPrincipal principal,
            CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            return ToHttp(await personal.GetItemAsync(userId, itemId, ct));
        });

        files.MapPatch("/items/{itemId:guid}", async Task<IResult> (
            Guid itemId,
            RenameItemRequest body,
            IPersonalService personal,
            ClaimsPrincipal principal,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            return ToHttp(await personal.RenameItemAsync(userId, itemId, body, Ctx(http), ct));
        });

        files.MapPost("/items/{itemId:guid}/move", async Task<IResult> (
            Guid itemId,
            MoveItemRequest body,
            IPersonalService personal,
            ClaimsPrincipal principal,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            return ToHttp(await personal.MoveItemAsync(userId, itemId, body, Ctx(http), ct));
        });

        files.MapDelete("/items/{itemId:guid}", async Task<IResult> (
            Guid itemId,
            IPersonalService personal,
            ClaimsPrincipal principal,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            return ToHttp(await personal.SoftDeleteItemAsync(userId, itemId, Ctx(http), ct));
        });

        files.MapPost("/items/{itemId:guid}/restore", async Task<IResult> (
            Guid itemId,
            IPersonalService personal,
            ClaimsPrincipal principal,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            return ToHttp(await personal.RestoreItemAsync(userId, itemId, Ctx(http), ct));
        });

        files.MapPost("/items/{itemId:guid}/star", async Task<IResult> (
            Guid itemId,
            IPersonalService personal,
            ClaimsPrincipal principal,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            return ToHttp(await personal.StarAsync(userId, itemId, Ctx(http), ct));
        });

        files.MapDelete("/items/{itemId:guid}/star", async Task<IResult> (
            Guid itemId,
            IPersonalService personal,
            ClaimsPrincipal principal,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            return ToHttp(await personal.UnstarAsync(userId, itemId, Ctx(http), ct));
        });

        files.MapPost("/items/{itemId:guid}/copy-to-channel", async Task<IResult> (
            Guid itemId,
            CopyToChannelRequest body,
            IPersonalService personal,
            ClaimsPrincipal principal,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            return ToHttp(await personal.CopyToChannelAsync(userId, itemId, body, Ctx(http), ct));
        });

        files.MapGet("/items/{itemId:guid}/content", async Task<IResult> (
            Guid itemId,
            IPersonalService personal,
            ClaimsPrincipal principal,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

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

            var result = await personal.OpenDownloadAsync(userId, itemId, rangeStart, rangeEnd, ct);
            if (!result.Succeeded || result.Value is null)
            {
                return ToHttp(result);
            }

            var d = result.Value;
            var disposition = ContentTypeDetector.AllowInline(d.ContentType) ? "inline" : "attachment";
            http.Response.Headers.Append("X-Content-Type-Options", "nosniff");
            http.Response.Headers.ContentDisposition = $"{disposition}; filename=\"{d.FileName.Replace("\"", "")}\"";

            if (d.IsPartial && d.SizeBytes > 0)
            {
                http.Response.StatusCode = StatusCodes.Status206PartialContent;
                http.Response.Headers.ContentRange = $"bytes {d.RangeStart}-{d.RangeEndInclusive}/{d.SizeBytes}";
                http.Response.ContentType = d.ContentType;
                http.Response.ContentLength = d.RangeEndInclusive - d.RangeStart + 1;
                await d.Content.CopyToAsync(http.Response.Body, ct);
                await d.Content.DisposeAsync();
                return Results.Empty;
            }

            return Results.Stream(d.Content, d.ContentType, fileDownloadName: null, enableRangeProcessing: false);
        });

        files.MapPost("/uploads", async Task<IResult> (
            StartPersonalUploadRequest body,
            IPersonalService personal,
            ClaimsPrincipal principal,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            return ToHttp(await personal.StartUploadAsync(userId, body, Ctx(http), ct));
        });

        return app;
    }

    private static bool TryUser(ClaimsPrincipal principal, out Guid userId)
    {
        var raw = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out userId);
    }

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
        ErrorCodes.Forbidden => StatusCodes.Status403Forbidden,
        ErrorCodes.NotFound => StatusCodes.Status404NotFound,
        ErrorCodes.Conflict or ErrorCodes.NameConflict => StatusCodes.Status409Conflict,
        ErrorCodes.TeamQuotaExceeded or ErrorCodes.PersonalQuotaExceeded => StatusCodes.Status409Conflict,
        ErrorCodes.FileTooLarge => StatusCodes.Status413PayloadTooLarge,
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
