using System.Security.Claims;
using Channeled.Application.Identity;
using Channeled.Domain.Errors;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.RateLimiting;

namespace Channeled.Api.Auth;

public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth").WithTags("Auth");

        group.MapGet("/csrf", (IAntiforgery antiforgery, HttpContext http) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(http);
            return Results.Ok(new { token = tokens.RequestToken });
        }).AllowAnonymous();

        group.MapPost("/login", async Task<IResult> (
            LoginRequest body,
            IAuthService auth,
            HttpContext http,
            CancellationToken ct) =>
        {
            var result = await auth.LoginAsync(body, CreateContext(http), ct);
            return ToHttp(result);
        })
        .AllowAnonymous()
        .RequireRateLimiting("auth");

        group.MapPost("/logout", async Task<IResult> (
            IAuthService auth,
            ClaimsPrincipal principal,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (!TryGetUserId(principal, out var userId))
            {
                return Problem(ErrorCodes.Unauthorized, "Not authenticated.", StatusCodes.Status401Unauthorized);
            }

            var sessionId = TryGetSessionId(principal);
            var result = await auth.LogoutAsync(userId, sessionId, CreateContext(http, sessionId), ct);
            return ToHttp(result);
        }).RequireAuthorization();

        group.MapGet("/me", async Task<IResult> (
            IAuthService auth,
            ClaimsPrincipal principal,
            CancellationToken ct) =>
        {
            if (!TryGetUserId(principal, out var userId))
            {
                return Problem(ErrorCodes.Unauthorized, "Not authenticated.", StatusCodes.Status401Unauthorized);
            }

            return ToHttp(await auth.GetCurrentUserAsync(userId, ct));
        }).RequireAuthorization();

        group.MapGet("/sessions", async Task<IResult> (
            IAuthService auth,
            ClaimsPrincipal principal,
            CancellationToken ct) =>
        {
            if (!TryGetUserId(principal, out var userId))
            {
                return Problem(ErrorCodes.Unauthorized, "Not authenticated.", StatusCodes.Status401Unauthorized);
            }

            return ToHttp(await auth.ListSessionsAsync(userId, TryGetSessionId(principal), ct));
        }).RequireAuthorization();

        group.MapDelete("/sessions/{sessionId:guid}", async Task<IResult> (
            Guid sessionId,
            IAuthService auth,
            ClaimsPrincipal principal,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (!TryGetUserId(principal, out var userId))
            {
                return Problem(ErrorCodes.Unauthorized, "Not authenticated.", StatusCodes.Status401Unauthorized);
            }

            return ToHttp(await auth.RevokeSessionAsync(userId, sessionId, CreateContext(http, TryGetSessionId(principal)), ct));
        }).RequireAuthorization();

        group.MapPost("/change-password", async Task<IResult> (
            ChangePasswordRequest body,
            IAuthService auth,
            ClaimsPrincipal principal,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (!TryGetUserId(principal, out var userId))
            {
                return Problem(ErrorCodes.Unauthorized, "Not authenticated.", StatusCodes.Status401Unauthorized);
            }

            return ToHttp(await auth.ChangePasswordAsync(userId, body, CreateContext(http, TryGetSessionId(principal)), ct));
        }).RequireAuthorization();

        group.MapPost("/invitations", async Task<IResult> (
            CreateInvitationRequest body,
            IAuthService auth,
            ClaimsPrincipal principal,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (!TryGetUserId(principal, out var userId))
            {
                return Problem(ErrorCodes.Unauthorized, "Not authenticated.", StatusCodes.Status401Unauthorized);
            }

            var me = await auth.GetCurrentUserAsync(userId, ct);
            if (!me.Succeeded || me.Value is null || !me.Value.IsPlatformAdmin)
            {
                return Problem(ErrorCodes.Forbidden, "Platform admin required.", StatusCodes.Status403Forbidden);
            }

            var request = body with { InvitedByUserId = userId };
            return ToHttp(await auth.CreateInvitationAsync(request, CreateContext(http, TryGetSessionId(principal)), ct));
        })
        .RequireAuthorization()
        .RequireRateLimiting("auth");

        group.MapPost("/invitations/accept", async Task<IResult> (
            AcceptInvitationRequest body,
            IAuthService auth,
            HttpContext http,
            CancellationToken ct) =>
        {
            return ToHttp(await auth.AcceptInvitationAsync(body, CreateContext(http), ct));
        })
        .AllowAnonymous()
        .RequireRateLimiting("auth");

        group.MapPost("/forgot-password", async Task<IResult> (
            ForgotPasswordRequest body,
            IAuthService auth,
            HttpContext http,
            IHostEnvironment env,
            CancellationToken ct) =>
        {
            if (env.IsEnvironment("Testing"))
            {
                var debug = await auth.ForgotPasswordForTestsAsync(body, CreateContext(http), ct);
                return Results.Ok(new { status = "ok", debugToken = debug.Value });
            }

            await auth.ForgotPasswordAsync(body, CreateContext(http), ct);
            return Results.Ok(new { status = "ok" });
        })
        .AllowAnonymous()
        .RequireRateLimiting("auth");

        group.MapPost("/reset-password", async Task<IResult> (
            ResetPasswordRequest body,
            IAuthService auth,
            HttpContext http,
            CancellationToken ct) =>
        {
            return ToHttp(await auth.ResetPasswordAsync(body, CreateContext(http), ct));
        })
        .AllowAnonymous()
        .RequireRateLimiting("auth");

        return group;
    }

    private static RequestContext CreateContext(HttpContext http, Guid? sessionId = null) =>
        new(
            http.Connection.RemoteIpAddress?.ToString() ?? "",
            http.Request.Headers.UserAgent.ToString(),
            sessionId);

    private static bool TryGetUserId(ClaimsPrincipal principal, out Guid userId)
    {
        var raw = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out userId);
    }

    private static Guid? TryGetSessionId(ClaimsPrincipal principal)
    {
        var raw = principal.FindFirstValue("sid");
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    private static IResult ToHttp(Application.Abstractions.Results.AppResult result)
    {
        if (result.Succeeded)
        {
            return Results.Ok(new { status = "ok" });
        }

        return Problem(result.Code ?? ErrorCodes.Validation, result.Message ?? "Request failed.", MapStatus(result.Code));
    }

    private static IResult ToHttp<T>(Application.Abstractions.Results.AppResult<T> result)
    {
        if (result.Succeeded)
        {
            return Results.Ok(result.Value);
        }

        return Problem(result.Code ?? ErrorCodes.Validation, result.Message ?? "Request failed.", MapStatus(result.Code));
    }

    private static int MapStatus(string? code) => code switch
    {
        ErrorCodes.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorCodes.Forbidden => StatusCodes.Status403Forbidden,
        ErrorCodes.NotFound => StatusCodes.Status404NotFound,
        ErrorCodes.Conflict => StatusCodes.Status409Conflict,
        ErrorCodes.RateLimited => StatusCodes.Status429TooManyRequests,
        ErrorCodes.LockedOut => StatusCodes.Status423Locked,
        ErrorCodes.InvalidToken => StatusCodes.Status400BadRequest,
        ErrorCodes.CsrfRejected => StatusCodes.Status400BadRequest,
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
