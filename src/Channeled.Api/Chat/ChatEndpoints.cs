using System.Security.Claims;
using Channeled.Application.Abstractions.Results;
using Channeled.Application.Chat;
using Channeled.Application.Identity;
using Channeled.Domain.Errors;

namespace Channeled.Api.Chat;

public static class ChatEndpoints
{
    /// <summary>Rate-limit policy for POST message (partitioned per authenticated user).</summary>
    public const string PostRateLimitPolicy = "chat-post";

    public static IEndpointRouteBuilder MapChatEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/teams/{slug}/channels/{channelName}")
            .WithTags("Chat")
            .RequireAuthorization();

        group.MapPost("/messages", async Task<IResult> (
            string slug,
            string channelName,
            PostMessageRequest body,
            IChatService chat,
            ClaimsPrincipal principal,
            CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            var result = await chat.PostMessageAsync(userId, slug, channelName, body, ct);
            return result.Succeeded
                ? Results.Json(result.Value, statusCode: StatusCodes.Status201Created)
                : ToProblem(result.Code, result.Message);
        }).RequireRateLimiting(PostRateLimitPolicy);

        group.MapGet("/messages", async Task<IResult> (
            string slug,
            string channelName,
            string? cursor,
            int? limit,
            IChatService chat,
            ClaimsPrincipal principal,
            CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            var result = await chat.ListMessagesAsync(
                userId, slug, channelName, new ListMessagesQuery(cursor, limit ?? ChatOptions.DefaultPageSize), ct);
            return ToHttp(result);
        });

        group.MapDelete("/messages/{messageId:guid}", async Task<IResult> (
            string slug,
            string channelName,
            Guid messageId,
            IChatService chat,
            ClaimsPrincipal principal,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            var result = await chat.DeleteMessageAsync(userId, slug, channelName, messageId, Ctx(http), ct);
            return result.Succeeded
                ? Results.NoContent()
                : ToProblem(result.Code, result.Message);
        });

        group.MapGet("/presence", async Task<IResult> (
            string slug,
            string channelName,
            IChatService chat,
            ClaimsPrincipal principal,
            CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            return ToHttp(await chat.ListPresenceAsync(userId, slug, channelName, ct));
        });

        return app;
    }

    private static bool TryUser(ClaimsPrincipal principal, out Guid userId) =>
        Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    private static RequestContext Ctx(HttpContext http) =>
        new(http.Connection.RemoteIpAddress?.ToString() ?? "", http.Request.Headers.UserAgent.ToString());

    private static IResult Unauthorized() =>
        ToProblem(ErrorCodes.Unauthorized, "Not authenticated.");

    private static IResult ToHttp<T>(AppResult<T> result) =>
        result.Succeeded ? Results.Ok(result.Value) : ToProblem(result.Code, result.Message);

    private static IResult ToProblem(string? code, string? detail)
    {
        var resolved = code ?? ErrorCodes.Validation;
        var status = resolved switch
        {
            ErrorCodes.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorCodes.Forbidden => StatusCodes.Status403Forbidden,
            ErrorCodes.NotFound => StatusCodes.Status404NotFound,
            ErrorCodes.ChannelArchived or ErrorCodes.Conflict => StatusCodes.Status409Conflict,
            ErrorCodes.RateLimited => StatusCodes.Status429TooManyRequests,
            _ => StatusCodes.Status400BadRequest,
        };

        return Results.Json(
            new
            {
                type = $"https://channeled.local/errors/{resolved}",
                title = resolved,
                status,
                detail = detail ?? "Request failed.",
                code = resolved,
            },
            statusCode: status);
    }
}
