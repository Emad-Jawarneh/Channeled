using Channeled.Application.Abstractions.Realtime;
using Channeled.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Channeled.Infrastructure.Realtime;

public sealed class AuthStateChangedService(
    HubConnectionRegistry registry,
    IServiceScopeFactory scopeFactory,
    IOptions<RealtimeOptions> options,
    ILogger<AuthStateChangedService> logger) : BackgroundService, IAuthStateChanged
{
    public void NotifyUser(Guid userId) => registry.AbortUser(userId);

    public void NotifyChannel(Guid channelId) => registry.AbortChannel(channelId);

    public void NotifyTeam(Guid teamId)
    {
        _ = AbortTeamAsync(teamId);
    }

    private async Task AbortTeamAsync(Guid teamId)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var userIds = await db.TeamMembers.AsNoTracking()
                .Where(m => m.TeamId == teamId)
                .Select(m => m.UserId)
                .ToListAsync();
            foreach (var userId in userIds)
            {
                registry.AbortUser(userId);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "AuthStateChanged team abort failed for {TeamId}", teamId);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var period = TimeSpan.FromSeconds(Math.Max(1, options.Value.AuthStateSweepSeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(period, stoppingToken);
                await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "AuthStateChanged sweep failed");
            }
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var snapshot = registry.Snapshot();
        if (snapshot.Count == 0)
        {
            return;
        }

        var userIds = snapshot.Select(s => s.UserId).Distinct().ToList();
        var users = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Status })
            .ToDictionaryAsync(u => u.Id, ct);

        foreach (var entry in snapshot)
        {
            if (!users.TryGetValue(entry.UserId, out var user) || user.Status != "active")
            {
                try
                {
                    entry.Context.Abort();
                }
                catch
                {
                    // ignore
                }

                continue;
            }

            foreach (var channelId in entry.ChannelIds.ToList())
            {
                var allowed = await CanStillViewAsync(db, entry.UserId, channelId, ct);
                if (!allowed)
                {
                    try
                    {
                        entry.Context.Abort();
                    }
                    catch
                    {
                        // ignore
                    }

                    break;
                }
            }
        }
    }

    private static async Task<bool> CanStillViewAsync(
        ApplicationDbContext db,
        Guid userId,
        Guid channelId,
        CancellationToken ct)
    {
        var channel = await db.Channels.AsNoTracking()
            .Include(c => c.Team)
            .FirstOrDefaultAsync(c => c.Id == channelId, ct);
        if (channel?.Team is null || channel.ArchivedAt != null || channel.Team.ArchivedAt != null)
        {
            return false;
        }

        var membership = await db.TeamMembers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.TeamId == channel.TeamId && m.UserId == userId, ct);
        return membership is not null;
    }
}
