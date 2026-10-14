using Channeled.Application.Abstractions.Realtime;
using Channeled.Application.Abstractions.Time;
using Channeled.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Channeled.Infrastructure.Identity;

public static class SessionSecurity
{
    public static async Task RevokeSessionsAsync(
        ApplicationDbContext db,
        UserManager<AppUser> userManager,
        IAuthStateChanged? authStateChanged,
        IClock clock,
        AppUser user,
        Guid? keepSessionId,
        CancellationToken ct)
    {
        var sessions = await db.UserSessions
            .Where(s => s.UserId == user.Id && s.RevokedAt == null)
            .ToListAsync(ct);
        foreach (var session in sessions)
        {
            if (keepSessionId is Guid keep && session.Id == keep)
            {
                continue;
            }

            session.RevokedAt = clock.UtcNow;
        }

        await userManager.UpdateSecurityStampAsync(user);

        var pending = await db.PendingEmailChanges
            .Where(p => p.UserId == user.Id && p.ConsumedAt == null && p.CancelledAt == null)
            .ToListAsync(ct);
        foreach (var p in pending)
        {
            p.CancelledAt = clock.UtcNow;
        }

        var resets = await db.PasswordResetTokens
            .Where(t => t.UserId == user.Id && t.UsedAt == null)
            .ToListAsync(ct);
        foreach (var t in resets)
        {
            t.UsedAt = clock.UtcNow;
        }

        authStateChanged?.NotifyUser(user.Id);
    }
}
