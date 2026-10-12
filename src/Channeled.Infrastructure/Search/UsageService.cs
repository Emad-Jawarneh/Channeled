using Channeled.Application.Abstractions.Results;
using Channeled.Application.Search;
using Channeled.Domain.Authorization;
using Channeled.Domain.Errors;
using Channeled.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Channeled.Infrastructure.Search;

public sealed class UsageService(ApplicationDbContext db, IAuthorizer authorizer) : IUsageService
{
    public async Task<AppResult<TeamStorageDto>> GetTeamStorageAsync(
        Guid userId,
        string teamSlug,
        CancellationToken ct = default)
    {
        var team = await db.Teams.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == teamSlug, ct);
        if (team is null)
        {
            return AppResult<TeamStorageDto>.Fail(ErrorCodes.NotFound, "Team not found.");
        }

        var member = await db.TeamMembers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.TeamId == team.Id && m.UserId == userId, ct);
        if (member is null
            || !authorizer.Authorize(new AuthorizationContext
            {
                UserId = userId,
                Action = AuthAction.ViewTeam,
                IsTeamMember = true,
                TeamRole = member.Role,
                TeamArchived = team.ArchivedAt is not null,
            }).Allowed)
        {
            return AppResult<TeamStorageDto>.Fail(ErrorCodes.NotFound, "Team not found.");
        }

        return AppResult<TeamStorageDto>.Ok(new TeamStorageDto(team.QuotaBytes, team.UsageBytes));
    }
}
