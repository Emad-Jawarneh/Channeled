using System.Security.Claims;
using Channeled.Application.Abstractions.Results;
using Channeled.Application.Files;
using Channeled.Application.Identity;
using Channeled.Application.Personal;
using Channeled.Domain.Errors;
using Channeled.Api.Files;
using Channeled.Application.Abstractions.Storage;
using Channeled.Infrastructure.Files;
using Microsoft.Extensions.Options;

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

            return ToHttp(await personal.ListItemsAsync(
                userId,
                new ListItemsQuery(folderId, cursor, limit ?? 50, sort, type, null, trash ?? false, q),
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
            bool? download,
            IPersonalService personal,
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

            var result = await personal.ResolveDownloadAsync(userId, itemId, ct);
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
                new ContentResponseOptions(asAttachment, CountTowardsLimit: download == true, NoStore: false, userId),
                store,
                slots,
                downloadOptions.CurrentValue.PerStreamBytesPerSecond,
                ct);
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
        ErrorCodes.LockTimeout => StatusCodes.Status409Conflict,
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
