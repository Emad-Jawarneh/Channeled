using System.Security.Claims;
using Channeled.Application.Identity;
using Channeled.Application.Notifications;
using Channeled.Application.Teams;
using Channeled.Domain.Errors;

namespace Channeled.Api.Teams;

public static class TeamEndpoints
{
    public static RouteGroupBuilder MapTeamEndpoints(this IEndpointRouteBuilder app)
    {
        var teams = app.MapGroup("/api/v1/teams").WithTags("Teams").RequireAuthorization();

        teams.MapPost("/", async Task<IResult> (CreateTeamRequest body, ITeamService teamsSvc, ClaimsPrincipal principal, HttpContext http, CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            return ToHttp(await teamsSvc.CreateTeamAsync(userId, body, Ctx(http), ct));
        });

        teams.MapGet("/", async Task<IResult> (ITeamService teamsSvc, ClaimsPrincipal principal, CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            return ToHttp(await teamsSvc.ListMyTeamsAsync(userId, ct));
        });

        teams.MapGet("/{slug}", async Task<IResult> (string slug, ITeamService teamsSvc, ClaimsPrincipal principal, CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            return ToHttp(await teamsSvc.GetTeamAsync(userId, slug, ct));
        });

        teams.MapPut("/{slug}", async Task<IResult> (string slug, UpdateTeamRequest body, ITeamService teamsSvc, ClaimsPrincipal principal, HttpContext http, CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            return ToHttp(await teamsSvc.UpdateTeamAsync(userId, slug, body, Ctx(http), ct));
        });

        teams.MapPost("/{slug}/archive", async Task<IResult> (string slug, ITeamService teamsSvc, ClaimsPrincipal principal, HttpContext http, CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            return ToHttp(await teamsSvc.ArchiveTeamAsync(userId, slug, Ctx(http), ct));
        });

        teams.MapPost("/{slug}/delete", async Task<IResult> (
            string slug,
            DeleteTeamRequest? body,
            ITeamService teamsSvc,
            ClaimsPrincipal principal,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            return ToHttp(await teamsSvc.DeleteTeamAsync(userId, slug, body ?? new DeleteTeamRequest(), Ctx(http), ct));
        });

        teams.MapPost("/{slug}/transfer-ownership/{newOwnerUserId:guid}", async Task<IResult> (string slug, Guid newOwnerUserId, ITeamService teamsSvc, ClaimsPrincipal principal, HttpContext http, CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            return ToHttp(await teamsSvc.TransferOwnershipAsync(userId, slug, newOwnerUserId, Ctx(http), ct));
        });

        teams.MapGet("/{slug}/members", async Task<IResult> (string slug, ITeamService teamsSvc, ClaimsPrincipal principal, CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            return ToHttp(await teamsSvc.ListMembersAsync(userId, slug, ct));
        });

        teams.MapPut("/{slug}/members/{memberUserId:guid}/role", async Task<IResult> (string slug, Guid memberUserId, ChangeMemberRoleRequest body, ITeamService teamsSvc, ClaimsPrincipal principal, HttpContext http, CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            return ToHttp(await teamsSvc.ChangeMemberRoleAsync(userId, slug, memberUserId, body, Ctx(http), ct));
        });

        teams.MapDelete("/{slug}/members/{memberUserId:guid}", async Task<IResult> (string slug, Guid memberUserId, ITeamService teamsSvc, ClaimsPrincipal principal, HttpContext http, CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            return ToHttp(await teamsSvc.RemoveMemberAsync(userId, slug, memberUserId, Ctx(http), ct));
        });

        // ---- Email invitations are retired in favour of the team join link: 410 Gone. ----
        teams.MapPost("/{slug}/invitations", (string slug) =>
            Gone("Email invitations were replaced by the team join link."));

        teams.MapPost("/invitations/accept", () =>
            Gone("Email invitations were replaced by the team join link."));

        teams.MapPost("/{slug}/invitations/{invitationId:guid}/revoke", (string slug, Guid invitationId) =>
            Gone("Email invitations were replaced by the team join link."));

        teams.MapPost("/{slug}/channels", async Task<IResult> (string slug, CreateChannelRequest body, ITeamService teamsSvc, ClaimsPrincipal principal, HttpContext http, CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            return ToHttp(await teamsSvc.CreateChannelAsync(userId, slug, body, Ctx(http), ct));
        });

        teams.MapGet("/{slug}/channels", async Task<IResult> (string slug, ITeamService teamsSvc, ClaimsPrincipal principal, CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            return ToHttp(await teamsSvc.ListChannelsAsync(userId, slug, ct));
        });

        teams.MapGet("/{slug}/channels/{channelName}", async Task<IResult> (string slug, string channelName, ITeamService teamsSvc, ClaimsPrincipal principal, CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            return ToHttp(await teamsSvc.GetChannelAsync(userId, slug, channelName, ct));
        });

        teams.MapPut("/{slug}/channels/{channelName}", async Task<IResult> (
            string slug, string channelName, UpdateChannelRequest body, ITeamService teamsSvc, ClaimsPrincipal principal, HttpContext http, CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            return ToHttp(await teamsSvc.UpdateChannelAsync(userId, slug, channelName, body, Ctx(http), ct));
        });

        teams.MapPost("/{slug}/channels/{channelName}/archive", async Task<IResult> (string slug, string channelName, ITeamService teamsSvc, ClaimsPrincipal principal, HttpContext http, CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            return ToHttp(await teamsSvc.ArchiveChannelAsync(userId, slug, channelName, Ctx(http), ct));
        });

        // POST (not DELETE): disposition body (discard vs move files) must bind reliably.
        teams.MapPost("/{slug}/channels/{channelName}/delete", async Task<IResult> (
            string slug,
            string channelName,
            DeleteChannelRequest? body,
            ITeamService teamsSvc,
            ClaimsPrincipal principal,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            return ToHttp(await teamsSvc.DeleteChannelAsync(
                userId,
                slug,
                channelName,
                body ?? new DeleteChannelRequest(),
                Ctx(http),
                ct));
        });

        teams.MapGet("/{slug}/channels/{channelName}/members", async Task<IResult> (string slug, string channelName, ITeamService teamsSvc, ClaimsPrincipal principal, CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            return ToHttp(await teamsSvc.ListChannelMembersAsync(userId, slug, channelName, ct));
        });

        teams.MapPut("/{slug}/channels/{channelName}/members", async Task<IResult> (string slug, string channelName, UpsertChannelMemberRequest body, ITeamService teamsSvc, ClaimsPrincipal principal, HttpContext http, CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            return ToHttp(await teamsSvc.UpsertChannelMemberAsync(userId, slug, channelName, body, Ctx(http), ct));
        });

        teams.MapDelete("/{slug}/channels/{channelName}/members/{memberUserId:guid}", async Task<IResult> (string slug, string channelName, Guid memberUserId, ITeamService teamsSvc, ClaimsPrincipal principal, HttpContext http, CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            return ToHttp(await teamsSvc.RemoveChannelMemberAsync(userId, slug, channelName, memberUserId, Ctx(http), ct));
        });

        // ---- Per-channel join requests are retired (team-level join link + Restricted grants replace them): 410 Gone. ----
        const string channelJoinGone = "Channel join requests were replaced by team join links and channel grants.";
        teams.MapGet("/{slug}/channels/{channelName}/join", (string slug, string channelName) => Gone(channelJoinGone));
        teams.MapPost("/{slug}/channels/{channelName}/join-requests", (string slug, string channelName) => Gone(channelJoinGone));
        teams.MapGet("/{slug}/channels/{channelName}/join-requests", (string slug, string channelName) => Gone(channelJoinGone));
        teams.MapPost("/{slug}/channels/{channelName}/join-requests/{requestId:guid}/approve", (string slug, string channelName, Guid requestId) => Gone(channelJoinGone));
        teams.MapPost("/{slug}/channels/{channelName}/join-requests/{requestId:guid}/reject", (string slug, string channelName, Guid requestId) => Gone(channelJoinGone));

        // ---- Channel visibility + bulk grants ----
        teams.MapPut("/{slug}/channels/{channelName}/visibility", async Task<IResult> (
            string slug, string channelName, SetChannelVisibilityRequest body, ITeamService teamsSvc, ClaimsPrincipal principal, HttpContext http, CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            return ToHttp(await teamsSvc.SetChannelVisibilityAsync(userId, slug, channelName, body, Ctx(http), ct));
        });

        teams.MapPut("/{slug}/channels/{channelName}/members/bulk", async Task<IResult> (
            string slug, string channelName, BulkUpsertChannelMembersRequest body, ITeamService teamsSvc, ClaimsPrincipal principal, HttpContext http, CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            return ToHttp(await teamsSvc.BulkUpsertChannelMembersAsync(userId, slug, channelName, body, Ctx(http), ct));
        });

        // ---- Team settings (name/description Admin+, join mode + default role Owner) ----
        teams.MapPut("/{slug}/settings", async Task<IResult> (
            string slug, UpdateTeamSettingsRequest body, ITeamService teamsSvc, ClaimsPrincipal principal, HttpContext http, CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            return ToHttp(await teamsSvc.UpdateTeamSettingsAsync(userId, slug, body, Ctx(http), ct));
        });

        // ---- Team logo (Admin+ write; any member read) ----
        teams.MapGet("/{slug}/logo", async Task<IResult> (
            string slug, ITeamService teamsSvc, ClaimsPrincipal principal, HttpContext http, CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            var result = await teamsSvc.OpenTeamLogoAsync(userId, slug, ct);
            if (!result.Succeeded || result.Value is null)
            {
                return ToHttp(result);
            }

            http.Response.Headers.CacheControl = "private, max-age=3600";
            http.Response.Headers.Append("X-Content-Type-Options", "nosniff");
            return Results.File(result.Value.Content, result.Value.ContentType);
        });

        teams.MapPut("/{slug}/logo", async Task<IResult> (
            string slug, ITeamService teamsSvc, ClaimsPrincipal principal, HttpContext http, CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            var contentType = http.Request.ContentType ?? "";
            return ToHttp(await teamsSvc.SetTeamLogoAsync(
                userId,
                slug,
                http.Request.Body,
                contentType,
                http.Request.ContentLength,
                Ctx(http),
                ct));
        });

        teams.MapDelete("/{slug}/logo", async Task<IResult> (
            string slug, ITeamService teamsSvc, ClaimsPrincipal principal, HttpContext http, CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            return ToHttp(await teamsSvc.ClearTeamLogoAsync(userId, slug, Ctx(http), ct));
        });

        teams.MapPost("/{slug}/leave", async Task<IResult> (
            string slug, ITeamService teamsSvc, ClaimsPrincipal principal, HttpContext http, CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            return ToHttp(await teamsSvc.LeaveTeamAsync(userId, slug, Ctx(http), ct));
        });

        // ---- Join link (Admin+). The raw token is a secret: never cache these responses. ----
        teams.MapGet("/{slug}/join-link", async Task<IResult> (
            string slug, ITeamService teamsSvc, ClaimsPrincipal principal, HttpContext http, CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            NoStoreNoReferrer(http);
            return ToHttp(await teamsSvc.GetJoinLinkAsync(userId, slug, ct));
        });

        teams.MapPost("/{slug}/join-link", async Task<IResult> (
            string slug, ITeamService teamsSvc, ClaimsPrincipal principal, HttpContext http, CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            NoStoreNoReferrer(http);
            return ToHttp(await teamsSvc.EnsureJoinLinkAsync(userId, slug, Ctx(http), ct));
        });

        teams.MapPost("/{slug}/join-link/rotate", async Task<IResult> (
            string slug, RotateTeamJoinLinkRequest? body, ITeamService teamsSvc, ClaimsPrincipal principal, HttpContext http, CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            NoStoreNoReferrer(http);
            return ToHttp(await teamsSvc.RotateJoinLinkAsync(userId, slug, body ?? new RotateTeamJoinLinkRequest(null), Ctx(http), ct));
        });

        // ---- Join requests ----
        teams.MapGet("/{slug}/join-requests", async Task<IResult> (
            string slug, ITeamService teamsSvc, ClaimsPrincipal principal, CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            return ToHttp(await teamsSvc.ListJoinRequestsAsync(userId, slug, ct));
        });

        teams.MapPost("/{slug}/join-requests/{requestId:guid}/approve", async Task<IResult> (
            string slug, Guid requestId, ApproveTeamJoinRequestRequest? body, ITeamService teamsSvc, ClaimsPrincipal principal, HttpContext http, CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            return ToHttp(await teamsSvc.ApproveJoinRequestAsync(userId, slug, requestId, body, Ctx(http), ct));
        });

        teams.MapPost("/{slug}/join-requests/{requestId:guid}/reject", async Task<IResult> (
            string slug, Guid requestId, ITeamService teamsSvc, ClaimsPrincipal principal, HttpContext http, CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            return ToHttp(await teamsSvc.RejectJoinRequestAsync(userId, slug, requestId, Ctx(http), ct));
        });

        // Requester withdraws their own request (no slug needed).
        teams.MapPost("/join-requests/{requestId:guid}/withdraw", async Task<IResult> (
            Guid requestId, ITeamService teamsSvc, ClaimsPrincipal principal, HttpContext http, CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            return ToHttp(await teamsSvc.WithdrawJoinRequestAsync(userId, requestId, Ctx(http), ct));
        });

        teams.MapGet("/pending-join-count", async Task<IResult> (
            INotificationService notifications, ClaimsPrincipal principal, CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            return ToHttp(await notifications.GetPendingTeamJoinCountAsync(userId, ct));
        });

        // ---- Join by link token. GET is anonymous-friendly (name + mode only); POST requires a session. ----
        // Both set Referrer-Policy: no-referrer so the token never leaks via Referer headers.
        var join = app.MapGroup("/api/v1/teams/join")
            .WithTags("TeamJoin")
            .RequireRateLimiting(JoinRateLimitPolicy);

        join.MapGet("/{token}", async Task<IResult> (
            string token, ITeamService teamsSvc, ClaimsPrincipal principal, HttpContext http, CancellationToken ct) =>
        {
            NoStoreNoReferrer(http);
            Guid? userId = TryUser(principal, out var id) ? id : null;
            return ToHttp(await teamsSvc.GetJoinInfoAsync(userId, token, ct));
        }).AllowAnonymous();

        join.MapPost("/{token}", async Task<IResult> (
            string token, ITeamService teamsSvc, ClaimsPrincipal principal, HttpContext http, CancellationToken ct) =>
        {
            NoStoreNoReferrer(http);
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            return ToHttp(await teamsSvc.JoinAsync(userId, token, Ctx(http), ct));
        }).RequireAuthorization();

        return teams;
    }

    public const string JoinRateLimitPolicy = "team-join";

    private static void NoStoreNoReferrer(HttpContext http)
    {
        http.Response.Headers["Referrer-Policy"] = "no-referrer";
        http.Response.Headers.CacheControl = "no-store";
    }

    private static IResult Gone(string detail) =>
        Problem(ErrorCodes.Gone, detail, StatusCodes.Status410Gone);

    private static bool TryUser(ClaimsPrincipal principal, out Guid userId)
    {
        var raw = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out userId);
    }

    private static RequestContext Ctx(HttpContext http) =>
        new(http.Connection.RemoteIpAddress?.ToString() ?? "", http.Request.Headers.UserAgent.ToString());

    private static IResult Unauthorized() =>
        Problem(ErrorCodes.Unauthorized, "Not authenticated.", StatusCodes.Status401Unauthorized);

    private static IResult ToHttp(Application.Abstractions.Results.AppResult result) =>
        result.Succeeded
            ? Results.Ok(new { status = "ok" })
            : Problem(result.Code ?? ErrorCodes.Validation, result.Message ?? "Request failed.", MapStatus(result.Code));

    private static IResult ToHttp<T>(Application.Abstractions.Results.AppResult<T> result) =>
        result.Succeeded
            ? Results.Ok(result.Value)
            : Problem(result.Code ?? ErrorCodes.Validation, result.Message ?? "Request failed.", MapStatus(result.Code));

    private static int MapStatus(string? code) => code switch
    {
        ErrorCodes.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorCodes.Forbidden => StatusCodes.Status403Forbidden,
        ErrorCodes.NotFound => StatusCodes.Status404NotFound,
        ErrorCodes.Conflict => StatusCodes.Status409Conflict,
        ErrorCodes.LockTimeout => StatusCodes.Status409Conflict,
        ErrorCodes.OwnerMustTransfer => StatusCodes.Status409Conflict,
        ErrorCodes.JoinLinkUnavailable => StatusCodes.Status409Conflict,
        ErrorCodes.OpenJoinTooManyPending => StatusCodes.Status409Conflict,
        ErrorCodes.JoinLinkInvalid => StatusCodes.Status404NotFound,
        ErrorCodes.JoinCooldown => StatusCodes.Status429TooManyRequests,
        ErrorCodes.JoinRateLimited => StatusCodes.Status429TooManyRequests,
        ErrorCodes.Gone => StatusCodes.Status410Gone,
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
