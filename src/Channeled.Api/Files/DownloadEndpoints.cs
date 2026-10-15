using System.Security.Claims;
using Channeled.Application.Abstractions.Storage;
using Channeled.Application.Abstractions.Time;
using Channeled.Application.Files;
using Channeled.Domain.Errors;
using Microsoft.Extensions.Options;

namespace Channeled.Api.Files;

/// <summary>
/// Download tickets: an authenticated call (so session expiry and access errors surface in the app) returns
/// short-lived URLs that the browser then downloads natively with progress and resume.
/// </summary>
public static class DownloadEndpoints
{
    public static IEndpointRouteBuilder MapDownloadEndpoints(this IEndpointRouteBuilder app)
    {
        var downloads = app.MapGroup("/api/v1/downloads").WithTags("Downloads").RequireAuthorization();

        downloads.MapPost("/tickets", async Task<IResult> (
            DownloadTicketsRequest body,
            IDownloadTicketService tickets,
            ClaimsPrincipal principal,
            CancellationToken ct) =>
        {
            if (!TryUserAndSession(principal, out var userId, out var sessionId))
            {
                return Problem(ErrorCodes.Unauthorized, "Not authenticated.", StatusCodes.Status401Unauthorized);
            }

            var result = await tickets.IssueAsync(userId, sessionId, body, ct);
            return result.Succeeded
                ? Results.Ok(result.Value)
                : Problem(result.Code ?? ErrorCodes.Validation, result.Message ?? "Request failed.", StatusCodes.Status400BadRequest);
        });

        downloads.MapGet("/{ticket}", async Task<IResult> (
            string ticket,
            IDownloadTicketService tickets,
            IFileStore store,
            IDownloadSlots slots,
            IOptionsMonitor<DownloadOptions> options,
            IClock clock,
            ClaimsPrincipal principal,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (!TryUserAndSession(principal, out var userId, out var sessionId))
            {
                return Problem(ErrorCodes.Unauthorized, "Not authenticated.", StatusCodes.Status401Unauthorized);
            }

            var resolved = await tickets.ResolveAsync(userId, sessionId, ticket, ct);
            if (!resolved.Succeeded || resolved.Value is null)
            {
                return Problem(ErrorCodes.NotFound, "Download not found.", StatusCodes.Status404NotFound);
            }

            var target = resolved.Value.Target;
            var decision = ContentResponder.Evaluate(http.Request, target);
            var limits = options.CurrentValue;
            // Fresh downloads must start right after the app issued the ticket; browser resumes may come later.
            if (!decision.IsResume && clock.UtcNow - resolved.Value.IssuedAt > TimeSpan.FromSeconds(limits.TicketStartSeconds))
            {
                return Problem(
                    ErrorCodes.DownloadTicketExpired,
                    "This download link has expired. Start the download again from Channeled.",
                    StatusCodes.Status410Gone);
            }

            return await ContentResponder.WriteAsync(
                http,
                target,
                decision,
                new ContentResponseOptions(AsAttachment: true, CountTowardsLimit: true, NoStore: true, userId),
                store,
                slots,
                limits.PerStreamBytesPerSecond,
                ct);
        });

        return app;
    }

    private static bool TryUserAndSession(ClaimsPrincipal principal, out Guid userId, out Guid sessionId)
    {
        sessionId = Guid.Empty;
        return Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out userId)
               && Guid.TryParse(principal.FindFirstValue("sid"), out sessionId);
    }

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
