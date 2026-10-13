using Channeled.Application.Abstractions.Time;
using Channeled.Application.Chat;
using Channeled.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Channeled.Infrastructure.Chat;

public sealed class ChatPurgeService(
    ApplicationDbContext db,
    IClock clock,
    IOptionsMonitor<ChatOptions> options) : IChatPurgeService
{
    public async Task<int> PurgeExpiredAsync(CancellationToken ct = default)
    {
        var retentionDays = Math.Max(0, options.CurrentValue.DeletedRetentionDays);
        var cutoff = clock.UtcNow.AddDays(-retentionDays);

        // Only already-scrubbed tombstones past retention. Live messages are never touched, and
        // audit rows are append-only so the ChannelMessageDelete trail survives the purge.
        return await db.ChannelMessages
            .Where(m => m.DeletedAt != null && m.DeletedAt < cutoff)
            .ExecuteDeleteAsync(ct);
    }
}
