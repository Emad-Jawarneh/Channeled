using Channeled.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Channeled.Infrastructure.Identity;

public static class UserVisibility
{
    public static async Task<bool> CanViewProfileAsync(
        ApplicationDbContext db,
        Guid viewerId,
        Guid targetUserId,
        bool viewerIsPlatformAdmin,
        CancellationToken ct)
    {
        if (viewerId == targetUserId || viewerIsPlatformAdmin)
        {
            return true;
        }

        return await db.TeamMembers.AsNoTracking()
            .Where(m => m.UserId == viewerId)
            .Select(m => m.TeamId)
            .Intersect(
                db.TeamMembers.AsNoTracking()
                    .Where(m => m.UserId == targetUserId)
                    .Select(m => m.TeamId))
            .AnyAsync(ct);
    }
}
