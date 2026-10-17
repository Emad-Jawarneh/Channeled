using Channeled.Application.Admin;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Channeled.Infrastructure.Admin;

public sealed class UserDeleteSweepHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<UserDeleteOptions> options,
    ILogger<UserDeleteSweepHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TimeSpan.FromSeconds(Math.Clamp(options.Value.SweepIntervalSeconds, 5, 3600));
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var admin = scope.ServiceProvider.GetRequiredService<IAdminService>();
                var n = await admin.SweepStuckUserDeletesAsync(stoppingToken).ConfigureAwait(false);
                if (n > 0)
                {
                    logger.LogInformation("User delete sweep advanced {Count} users", n);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "User delete sweep failed");
            }

            await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
        }
    }
}
