using System.Security.Claims;
using Channeled.Application.Identity;
using Channeled.Domain.Errors;
using Microsoft.AspNetCore.RateLimiting;

namespace Channeled.Api.Account;

public static class AccountMailEndpoints
{
    public static RouteGroupBuilder MapAccountMailEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/account").WithTags("AccountMail");

        group.MapPost("/email-change", async Task<IResult> (
            StartEmailChangeRequest body,
            IAccountMailService mail,
            ClaimsPrincipal principal,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            return ToHttp(await mail.StartEmailChangeAsync(userId, body, Ctx(http), ct));
        }).RequireAuthorization().RequireRateLimiting("auth");

        // Token-authenticated POSTs: CSRF-exempt (configured in Program), token required in body.
        group.MapPost("/email-change/confirm", async Task<IResult> (
            ConfirmEmailChangeRequest body,
            IAccountMailService mail,
            HttpContext http,
            CancellationToken ct) =>
            ToHttp(await mail.ConfirmEmailChangeAsync(body, Ctx(http), ct)))
            .AllowAnonymous()
            .RequireRateLimiting("auth-token");

        group.MapPost("/email-change/cancel", async Task<IResult> (
            CancelEmailChangeRequest body,
            IAccountMailService mail,
            HttpContext http,
            CancellationToken ct) =>
            ToHttp(await mail.CancelEmailChangeAsync(body, Ctx(http), ct)))
            .AllowAnonymous()
            .RequireRateLimiting("auth-token");

        group.MapPost("/email-change/sign-out-all", async Task<IResult> (
            CancelEmailChangeRequest body,
            IAccountMailService mail,
            HttpContext http,
            CancellationToken ct) =>
            ToHttp(await mail.SignOutEverywhereWithCancelTokenAsync(body, Ctx(http), ct)))
            .AllowAnonymous()
            .RequireRateLimiting("auth-token");

        group.MapPost("/email-change/password", async Task<IResult> (
            CancelTokenPasswordChangeRequest body,
            IAccountMailService mail,
            HttpContext http,
            CancellationToken ct) =>
            ToHttp(await mail.ChangePasswordWithCancelTokenAsync(body, Ctx(http), ct)))
            .AllowAnonymous()
            .RequireRateLimiting("auth-token");

        group.MapGet("/self-service-mail", async Task<IResult> (
            ISelfServiceMail selfService,
            CancellationToken ct) =>
        {
            var available = await selfService.IsAvailableAsync(ct);
            var probe = await selfService.LastSmtpProbeAsync(ct);
            return Results.Ok(new { available, lastSmtpProbeAt = probe });
        }).RequireAuthorization();

        return group;
    }

    private static RequestContext Ctx(HttpContext http)
    {
        Guid? sid = null;
        var raw = http.User.FindFirstValue("sid");
        if (raw is not null && Guid.TryParse(raw, out var parsed))
        {
            sid = parsed;
        }

        return new RequestContext(
            http.Connection.RemoteIpAddress?.ToString() ?? "",
            http.Request.Headers.UserAgent.ToString(),
            sid);
    }

    private static bool TryUser(ClaimsPrincipal principal, out Guid userId)
    {
        userId = default;
        var raw = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return raw is not null && Guid.TryParse(raw, out userId);
    }

    private static IResult Unauthorized() =>
        Results.Problem(detail: "Not authenticated.", statusCode: 401, extensions: new Dictionary<string, object?> { ["code"] = ErrorCodes.Unauthorized });

    private static IResult ToHttp(Channeled.Application.Abstractions.Results.AppResult result)
    {
        if (result.Succeeded)
        {
            return Results.Ok();
        }

        var status = result.Code switch
        {
            ErrorCodes.Unauthorized => 401,
            ErrorCodes.Forbidden => 403,
            ErrorCodes.NotFound => 404,
            ErrorCodes.Conflict => 409,
            ErrorCodes.InvalidToken => 400,
            _ => 400,
        };
        return Results.Problem(
            detail: result.Message,
            statusCode: status,
            extensions: new Dictionary<string, object?> { ["code"] = result.Code });
    }
}
