using System.Security.Claims;
using Channeled.Application.Abstractions.Results;
using Channeled.Application.Notifications;
using Channeled.Domain.Errors;

namespace Channeled.Api.Notifications;

public static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/notifications")
            .WithTags("Notifications")
            .RequireAuthorization();

        group.MapGet("/", async Task<IResult> (
            int? limit,
            bool? unreadOnly,
            INotificationService notifications,
            ClaimsPrincipal principal,
            CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            return ToHttp(await notifications.ListAsync(userId, limit ?? 50, unreadOnly ?? false, ct));
        });

        group.MapGet("/unread-count", async Task<IResult> (
            INotificationService notifications,
            ClaimsPrincipal principal,
            CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            return ToHttp(await notifications.UnreadCountAsync(userId, ct));
        });

        group.MapPost("/{id:guid}/read", async Task<IResult> (
            Guid id,
            INotificationService notifications,
            ClaimsPrincipal principal,
            CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            return ToHttp(await notifications.MarkReadAsync(userId, id, ct));
        });

        group.MapPost("/read-all", async Task<IResult> (
            INotificationService notifications,
            ClaimsPrincipal principal,
            CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            return ToHttp(await notifications.MarkAllReadAsync(userId, ct));
        });

        return app;
    }

    private static bool TryUser(ClaimsPrincipal principal, out Guid userId)
    {
        var raw = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out userId);
    }

    private static IResult Unauthorized() =>
        Results.Json(
            new
            {
                type = $"https://channeled.local/errors/{ErrorCodes.Unauthorized}",
                title = ErrorCodes.Unauthorized,
                status = 401,
                detail = "Not authenticated.",
                code = ErrorCodes.Unauthorized,
            },
            statusCode: StatusCodes.Status401Unauthorized);

    private static IResult ToHttp(AppResult result) =>
        result.Succeeded
            ? Results.Ok(new { status = "ok" })
            : Problem(result.Code ?? ErrorCodes.Validation, result.Message ?? "Request failed.");

    private static IResult ToHttp<T>(AppResult<T> result) =>
        result.Succeeded
            ? Results.Ok(result.Value)
            : Problem(result.Code ?? ErrorCodes.Validation, result.Message ?? "Request failed.");

    private static IResult Problem(string code, string detail)
    {
        var status = code switch
        {
            ErrorCodes.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorCodes.Forbidden => StatusCodes.Status403Forbidden,
            ErrorCodes.NotFound => StatusCodes.Status404NotFound,
            _ => StatusCodes.Status400BadRequest,
        };
        return Results.Json(
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
}
