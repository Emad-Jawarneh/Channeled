using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Channeled.Infrastructure.Files;

public sealed class TrashPurgeHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<TrashPurgeHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(20), stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var purge = scope.ServiceProvider.GetRequiredService<ITrashPurgeService>();
                var removed = await purge.PurgeExpiredAsync(stoppingToken).ConfigureAwait(false);
                logger.LogInformation("Trash purge cycle removed={Removed}", removed);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Trash purge failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromHours(24), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}