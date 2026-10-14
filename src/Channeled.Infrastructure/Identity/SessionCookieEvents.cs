using Channeled.Application.Identity;
using Channeled.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Channeled.Infrastructure.Identity;

public static class SessionCookieEvents
{
    public static async Task ValidatePrincipalAsync(CookieValidatePrincipalContext context)
    {
        var sid = context.Principal?.FindFirst("sid")?.Value;
        if (sid is null || !Guid.TryParse(sid, out var sessionId))
        {
            context.RejectPrincipal();
            return;
        }

        var account = context.HttpContext.RequestServices.GetRequiredService<IOptionsMonitor<AccountOptions>>().CurrentValue;
        var ttlSeconds = Math.Clamp(account.SessionCacheTtlSeconds, 0, 5);
        var cache = context.HttpContext.RequestServices.GetRequiredService<SessionValidationCache>();
        var cached = ttlSeconds > 0 ? cache.TryGet(sessionId) : null;
        if (cached is false)
        {
            context.RejectPrincipal();
            return;
        }

        if (cached is true)
        {
            return;
        }

        var db = context.HttpContext.RequestServices.GetRequiredService<ApplicationDbContext>();
        var ok = await db.UserSessions.AsNoTracking()
            .AnyAsync(s => s.Id == sessionId && s.RevokedAt == null, context.HttpContext.RequestAborted);
        if (ttlSeconds > 0)
        {
            cache.Set(sessionId, ok, TimeSpan.FromSeconds(ttlSeconds));
        }

        if (!ok)
        {
            context.RejectPrincipal();
        }
    }
}
