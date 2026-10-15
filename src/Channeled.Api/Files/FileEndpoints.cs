using System.Security.Claims;
using Channeled.Application.Abstractions.Results;
using Channeled.Application.Files;
using Channeled.Application.Identity;
using Channeled.Domain.Errors;
using Channeled.Application.Abstractions.Storage;
using Channeled.Infrastructure.Files;
using Microsoft.Extensions.Options;

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
            string? q,
            CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            if (q is { Length: > 200 })
            {
                return Problem(ErrorCodes.Validation, "q must be at most 200 characters.", StatusCodes.Status400BadRequest);
            }

            var query = new ListItemsQuery(
                folderId,
                cursor,
                limit ?? 50,
                sort,
                type,
                uploaderId,
                trash ?? false,
                q);
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
            bool? download,
            IFileService files,
            IFileStore store,
            IDownloadSlots slots,
            IOptionsMonitor<DownloadOptions> downloadOptions,
            ClaimsPrincipal principal,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            var result = await files.ResolveDownloadAsync(userId, slug, channelName, itemId, ct);
            if (!result.Succeeded || result.Value is null)
            {
                return ToHttp(result);
            }

            var target = result.Value;
            var asAttachment = download == true || !ContentTypeDetector.AllowInline(target.ContentType);
            return await ContentResponder.WriteAsync(
                http,
                target,
                ContentResponder.Evaluate(http.Request, target),
                // Explicit downloads count towards the concurrent limit; inline previews/thumbnails do not.
                new ContentResponseOptions(asAttachment, CountTowardsLimit: download == true, NoStore: false, userId),
                store,
                slots,
                downloadOptions.CurrentValue.PerStreamBytesPerSecond,
                ct);
        });

        var items = app.MapGroup("/api/v1/items").WithTags("Files").RequireAuthorization();

        // One call for every "open this item" path (chat chips, search, deep links), however deep its page is.
        items.MapGet("/{itemId:guid}/locate", async Task<IResult> (
            Guid itemId,
            IFileService files,
            Channeled.Application.Personal.IPersonalService personal,
            ClaimsPrincipal principal,
            CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            var mine = await personal.LocateItemAsync(userId, itemId, ct);
            return ToHttp(mine.Succeeded ? mine : await files.LocateItemAsync(userId, itemId, ct));
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

        var teamFiles = app.MapGroup("/api/v1/teams/{slug}/files")
            .WithTags("Files")
            .RequireAuthorization();

        teamFiles.MapPost("/resolve", async Task<IResult> (
            string slug,
            ResolveFilesRequest body,
            IFileReferenceService fileRefs,
            ClaimsPrincipal principal,
            CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            return ToHttp(await fileRefs.ResolveAsync(userId, slug, body, ct));
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
        ErrorCodes.LockTimeout => StatusCodes.Status409Conflict,
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
