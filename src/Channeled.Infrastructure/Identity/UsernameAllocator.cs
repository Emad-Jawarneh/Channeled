using Channeled.Domain.Identity;
using Channeled.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Channeled.Infrastructure.Identity;

public static class UsernameAllocator
{
    public static async Task<string> AllocateUniqueAsync(
        UserManager<AppUser> userManager,
        ApplicationDbContext db,
        string seed,
        CancellationToken ct = default)
    {
        var baseSlug = UsernameRules.SuggestFrom(seed);
        if (!UsernameRules.TryValidate(baseSlug, out var candidate, out _))
        {
            candidate = "user";
        }

        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 1000; i++)
        {
            var trySlug = i == 0 ? candidate : Truncate($"{candidate}{i + 1}", UsernameRules.MaxLength);
            if (!UsernameRules.TryValidate(trySlug, out var normalized, out _))
            {
                continue;
            }

            var takenByUser = await userManager.Users.AsNoTracking()
                .AnyAsync(u => u.UsernameSlug == normalized, ct);
            if (takenByUser)
            {
                continue;
            }

            var held = await db.UsernameReservations.AsNoTracking()
                .AnyAsync(r => r.NormalizedUsername == normalized && r.ExpiresAt > now, ct);
            if (held)
            {
                continue;
            }

            return normalized;
        }

        return Truncate($"u{Guid.NewGuid():N}", UsernameRules.MaxLength);
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max].TrimEnd('.', '_', '-');
}
