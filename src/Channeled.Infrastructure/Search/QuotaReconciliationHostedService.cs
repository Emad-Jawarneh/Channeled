using Channeled.Application.Search;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Channeled.Infrastructure.Search;

/// <summary>Nightly-ish quota reconciliation (every 24h after a short startup delay).</summary>
public sealed class QuotaReconciliationHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<QuotaReconciliationHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
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
                var svc = scope.ServiceProvider.GetRequiredService<IQuotaReconciliationService>();
                var result = await svc.ReconcileAsync(stoppingToken);
                logger.LogInformation(
                    "Quota reconciliation completed: teams={Teams} users={Users}",
                    result.TeamsUpdated,
                    result.UsersUpdated);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Quota reconciliation failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
