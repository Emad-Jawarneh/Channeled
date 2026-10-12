using System.Security.Claims;
using Channeled.Application.Identity;
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

        teams.MapDelete("/{slug}", async Task<IResult> (string slug, ITeamService teamsSvc, ClaimsPrincipal principal, HttpContext http, CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            return ToHttp(await teamsSvc.DeleteTeamAsync(userId, slug, Ctx(http), ct));
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

        teams.MapPost("/{slug}/invitations", async Task<IResult> (string slug, CreateTeamInvitationRequest body, ITeamService teamsSvc, ClaimsPrincipal principal, HttpContext http, CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            return ToHttp(await teamsSvc.InviteAsync(userId, slug, body, Ctx(http), ct));
        });

        teams.MapPost("/invitations/accept", async Task<IResult> (AcceptTeamInvitationRequest body, ITeamService teamsSvc, ClaimsPrincipal principal, HttpContext http, CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            return ToHttp(await teamsSvc.AcceptInvitationAsync(userId, body, Ctx(http), ct));
        });

        teams.MapPost("/{slug}/invitations/{invitationId:guid}/revoke", async Task<IResult> (
            string slug, Guid invitationId, ITeamService teamsSvc, ClaimsPrincipal principal, HttpContext http, CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            return ToHttp(await teamsSvc.RevokeInvitationAsync(userId, slug, invitationId, Ctx(http), ct));
        });

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

        teams.MapDelete("/{slug}/channels/{channelName}", async Task<IResult> (string slug, string channelName, ITeamService teamsSvc, ClaimsPrincipal principal, HttpContext http, CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId))
            {
                return Unauthorized();
            }

            return ToHttp(await teamsSvc.DeleteChannelAsync(userId, slug, channelName, Ctx(http), ct));
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

        return teams;
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
