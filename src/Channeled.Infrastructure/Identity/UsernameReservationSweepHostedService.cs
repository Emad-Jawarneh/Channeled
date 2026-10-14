using Channeled.Application.Abstractions.Time;
using Channeled.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Channeled.Infrastructure.Identity;

public sealed class UsernameReservationSweepHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<UsernameReservationSweepHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var clock = scope.ServiceProvider.GetRequiredService<IClock>();
                var expired = await db.UsernameReservations
                    .Where(r => r.ExpiresAt <= clock.UtcNow)
                    .ToListAsync(stoppingToken);
                if (expired.Count > 0)
                {
                    db.UsernameReservations.RemoveRange(expired);
                    await db.SaveChangesAsync(stoppingToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "username reservation sweep failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromHours(6), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
