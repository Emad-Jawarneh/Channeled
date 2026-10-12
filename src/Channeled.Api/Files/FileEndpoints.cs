using System.Security.Claims;
using Channeled.Application.Abstractions.Results;
using Channeled.Application.Files;
using Channeled.Application.Identity;
using Channeled.Domain.Errors;
using Channeled.Infrastructure.Files;
using Microsoft.Net.Http.Headers;

namespace Channeled.Api.Files;

public static class FileEndpoints
{
    public static IEndpointRouteBuilder MapFileEndpoints(this IEndpointRouteBuilder app)
    {
        var channelItems = app.MapGroup("/api/v1/teams/{slug}/channels/{channelName}")
            .WithTags("Files")
            .RequireAuthorization();

        channelItems.MapPost("/folders", async Task<IResult> (
            string slug,
            string channelName,
            CreateFolderRequest body,
            IFileService files,
            ClaimsPrincipal principal,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            return ToHttp(await files.CreateFolderAsync(userId, slug, channelName, body, Ctx(http), ct));
        });

        channelItems.MapGet("/items", async Task<IResult> (
            string slug,
            string channelName,
            IFileService files,
            ClaimsPrincipal principal,
            Guid? folderId,
            string? cursor,
            int? limit,
            string? sort,
            string? type,
            Guid? uploaderId,
            bool? trash,
            CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            var query = new ListItemsQuery(
                folderId,
                cursor,
                limit ?? 50,
                sort,
                type,
                uploaderId,
                trash ?? false);
            return ToHttp(await files.ListItemsAsync(userId, slug, channelName, query, ct));
        });

        channelItems.MapGet("/items/{itemId:guid}", async Task<IResult> (
            string slug,
            string channelName,
            Guid itemId,
            IFileService files,
            ClaimsPrincipal principal,
            CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            return ToHttp(await files.GetItemAsync(userId, slug, channelName, itemId, ct));
        });

        channelItems.MapPatch("/items/{itemId:guid}", async Task<IResult> (
            string slug,
            string channelName,
            Guid itemId,
            RenameItemRequest body,
            IFileService files,
            ClaimsPrincipal principal,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            return ToHttp(await files.RenameItemAsync(userId, slug, channelName, itemId, body, Ctx(http), ct));
        });

        channelItems.MapPost("/items/{itemId:guid}/move", async Task<IResult> (
            string slug,
            string channelName,
            Guid itemId,
            MoveItemRequest body,
            IFileService files,
            ClaimsPrincipal principal,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            return ToHttp(await files.MoveItemAsync(userId, slug, channelName, itemId, body, Ctx(http), ct));
        });

        channelItems.MapDelete("/items/{itemId:guid}", async Task<IResult> (
            string slug,
            string channelName,
            Guid itemId,
            IFileService files,
            ClaimsPrincipal principal,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            return ToHttp(await files.SoftDeleteItemAsync(userId, slug, channelName, itemId, Ctx(http), ct));
        });

        channelItems.MapPost("/items/{itemId:guid}/restore", async Task<IResult> (
            string slug,
            string channelName,
            Guid itemId,
            IFileService files,
            ClaimsPrincipal principal,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            return ToHttp(await files.RestoreItemAsync(userId, slug, channelName, itemId, Ctx(http), ct));
        });

        channelItems.MapGet("/items/{itemId:guid}/content", async Task<IResult> (
            string slug,
            string channelName,
            Guid itemId,
            IFileService files,
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

            var result = await files.OpenDownloadAsync(userId, slug, channelName, itemId, rangeStart, rangeEnd, ct);
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

        var uploads = app.MapGroup("/api/v1/uploads").WithTags("Uploads").RequireAuthorization();

        uploads.MapPost("/", async Task<IResult> (
            StartUploadRequest body,
            IFileService files,
            ClaimsPrincipal principal,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            return ToHttp(await files.StartUploadAsync(userId, body, Ctx(http), ct));
        });

        uploads.MapPut("/{uploadId:guid}/parts/{partNumber:int}", async Task<IResult> (
            Guid uploadId,
            int partNumber,
            IFileService files,
            ClaimsPrincipal principal,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            return ToHttp(await files.UploadPartAsync(userId, uploadId, partNumber, http.Request.Body, ct));
        });

        uploads.MapGet("/{uploadId:guid}/parts", async Task<IResult> (
            Guid uploadId,
            IFileService files,
            ClaimsPrincipal principal,
            CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            return ToHttp(await files.ListPartsAsync(userId, uploadId, ct));
        });

        uploads.MapPost("/{uploadId:guid}/complete", async Task<IResult> (
            Guid uploadId,
            CompleteUploadRequest? body,
            IFileService files,
            ClaimsPrincipal principal,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            var idempotency = http.Request.Headers["Idempotency-Key"].FirstOrDefault()
                              ?? body?.IdempotencyKey;
            var request = new CompleteUploadRequest(body?.ExpectedSha256Hex, idempotency);
            return ToHttp(await files.CompleteUploadAsync(userId, uploadId, request, Ctx(http), ct));
        });

        uploads.MapPost("/{uploadId:guid}/abort", async Task<IResult> (
            Guid uploadId,
            IFileService files,
            ClaimsPrincipal principal,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            return ToHttp(await files.AbortUploadAsync(userId, uploadId, Ctx(http), ct));
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
        ErrorCodes.InvalidToken => StatusCodes.Status400BadRequest,
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
