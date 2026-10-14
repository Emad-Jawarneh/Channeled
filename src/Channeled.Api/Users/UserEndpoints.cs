using System.Security.Claims;
using Channeled.Application.Identity;
using Channeled.Domain.Errors;
using Channeled.Infrastructure.Identity;

namespace Channeled.Api.Users;

public static class UserEndpoints
{
    public const long AvatarMaxBytes = AvatarImageProcessor.MaxBytes;

    public static RouteGroupBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/users").WithTags("Users").RequireAuthorization();

        group.MapGet("/{id:guid}/card", async Task<IResult> (
            Guid id,
            IProfileService profiles,
            ClaimsPrincipal principal,
            CancellationToken ct) =>
        {
            if (!TryGetUserId(principal, out var viewerId))
            {
                return Problem(ErrorCodes.Unauthorized, "Not authenticated.", StatusCodes.Status401Unauthorized);
            }

            return ToHttp(await profiles.GetCardAsync(viewerId, id, ct));
        });

        group.MapGet("/{id:guid}/avatar", async Task<IResult> (
            Guid id,
            IProfileService profiles,
            ClaimsPrincipal principal,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (!TryGetUserId(principal, out var viewerId))
            {
                return Problem(ErrorCodes.Unauthorized, "Not authenticated.", StatusCodes.Status401Unauthorized);
            }

            var result = await profiles.OpenAvatarAsync(viewerId, id, ct);
            if (!result.Succeeded || result.Value is null)
            {
                return ToHttp(result);
            }

            http.Response.Headers.CacheControl = "private, max-age=300";
            return Results.File(result.Value.Content, result.Value.ContentType);
        });

        return group;
    }

    public static RouteGroupBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/account").WithTags("Account").RequireAuthorization();

        group.MapPatch("/profile", async Task<IResult> (
            UpdateProfileRequest body,
            IProfileService profiles,
            ClaimsPrincipal principal,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (!TryGetUserId(principal, out var userId))
            {
                return Problem(ErrorCodes.Unauthorized, "Not authenticated.", StatusCodes.Status401Unauthorized);
            }

            return ToHttp(await profiles.UpdateProfileAsync(userId, body, CreateContext(http), ct));
        });

        group.MapGet("/username-available", async Task<IResult> (
            string username,
            IProfileService profiles,
            ClaimsPrincipal principal,
            CancellationToken ct) =>
        {
            if (!TryGetUserId(principal, out var userId))
            {
                return Problem(ErrorCodes.Unauthorized, "Not authenticated.", StatusCodes.Status401Unauthorized);
            }

            return ToHttp(await profiles.CheckUsernameAsync(userId, username, ct));
        });

        group.MapPut("/avatar", async Task<IResult> (
            HttpRequest request,
            IProfileService profiles,
            ClaimsPrincipal principal,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (!TryGetUserId(principal, out var userId))
            {
                return Problem(ErrorCodes.Unauthorized, "Not authenticated.", StatusCodes.Status401Unauthorized);
            }

            if (request.ContentLength is > AvatarMaxBytes)
            {
                return Problem(ErrorCodes.Validation, "Avatar must be 2 MB or smaller.", StatusCodes.Status400BadRequest);
            }

            http.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>()!.MaxRequestBodySize =
                AvatarMaxBytes;

            return ToHttp(await profiles.UploadAvatarAsync(
                userId,
                request.Body,
                request.ContentLength,
                CreateContext(http),
                ct));
        });

        group.MapDelete("/avatar", async Task<IResult> (
            IProfileService profiles,
            ClaimsPrincipal principal,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (!TryGetUserId(principal, out var userId))
            {
                return Problem(ErrorCodes.Unauthorized, "Not authenticated.", StatusCodes.Status401Unauthorized);
            }

            return ToHttp(await profiles.DeleteAvatarAsync(userId, CreateContext(http), ct));
        });

        return group;
    }

    private static RequestContext CreateContext(HttpContext http) =>
        new(
            http.Connection.RemoteIpAddress?.ToString() ?? "",
            http.Request.Headers.UserAgent.ToString());

    private static bool TryGetUserId(ClaimsPrincipal principal, out Guid userId)
    {
        userId = default;
        var raw = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return raw is not null && Guid.TryParse(raw, out userId);
    }

    private static IResult ToHttp<T>(Channeled.Application.Abstractions.Results.AppResult<T> result)
    {
        if (result.Succeeded)
        {
            return Results.Ok(result.Value);
        }

        return Problem(result.Code ?? ErrorCodes.Validation, result.Message ?? "Error", MapStatus(result.Code));
    }

    private static IResult ToHttp(Channeled.Application.Abstractions.Results.AppResult result)
    {
        if (result.Succeeded)
        {
            return Results.Ok();
        }

        return Problem(result.Code ?? ErrorCodes.Validation, result.Message ?? "Error", MapStatus(result.Code));
    }

    private static int MapStatus(string? code) => code switch
    {
        ErrorCodes.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorCodes.Forbidden => StatusCodes.Status403Forbidden,
        ErrorCodes.NotFound => StatusCodes.Status404NotFound,
        ErrorCodes.Conflict => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status400BadRequest,
    };

    private static IResult Problem(string code, string detail, int status) =>
        Results.Problem(detail: detail, statusCode: status, extensions: new Dictionary<string, object?> { ["code"] = code });
}
