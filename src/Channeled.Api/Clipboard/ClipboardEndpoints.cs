using System.Security.Claims;
using Channeled.Application.Abstractions.Results;
using Channeled.Application.Clipboard;
using Channeled.Application.Identity;
using Channeled.Domain.Errors;

namespace Channeled.Api.Clipboard;

public static class ClipboardEndpoints
{
    public static IEndpointRouteBuilder MapClipboardEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/clipboard/paste", async Task<IResult> (
            ClipboardPasteRequest body,
            IClipboardService clipboard,
            ClaimsPrincipal principal,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            return ToHttp(await clipboard.PasteAsync(userId, body, Ctx(http), ct));
        })
        .WithTags("Clipboard")
        .RequireAuthorization();

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

    private static IResult ToHttp(AppResult<ClipboardPasteResponse> result) =>
        result.Succeeded
            ? Results.Ok(result.Value)
            : Problem(result.Code ?? ErrorCodes.Validation, result.Message ?? "Request failed.", MapStatus(result.Code));

    private static int MapStatus(string? code) => code switch
    {
        ErrorCodes.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorCodes.Forbidden => StatusCodes.Status403Forbidden,
        ErrorCodes.NotFound => StatusCodes.Status404NotFound,
        ErrorCodes.TeamQuotaExceeded or ErrorCodes.PersonalQuotaExceeded => StatusCodes.Status409Conflict,
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
