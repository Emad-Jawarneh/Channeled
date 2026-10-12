using Channeled.Application.Abstractions.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Channeled.Infrastructure.Operations;

/// <summary>Daily backup runner; also checks low disk and alerts.</summary>
public sealed class BackupHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<BackupOptions> options,
    ILogger<BackupHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
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
                var health = scope.ServiceProvider.GetRequiredService<ISystemHealthService>();
                var report = await health.CheckAsync(stoppingToken);
                if (!report.Disk)
                {
                    var alerts = scope.ServiceProvider.GetRequiredService<IOperatorAlertService>();
                    await alerts.AlertAsync(
                        "Low disk",
                        $"Free disk bytes: {report.DiskFreeBytes}. Threshold: {options.Value.LowDiskBytes}.",
                        stoppingToken);
                }

                var backups = scope.ServiceProvider.GetRequiredService<IBackupService>();
                var kind = DateTime.UtcNow.Day == 1
                    ? "monthly"
                    : DateTime.UtcNow.DayOfWeek == DayOfWeek.Sunday ? "weekly" : "daily";
                var info = await backups.CreateAsync(kind, stoppingToken);
                logger.LogInformation("Scheduled backup {BackupId} ({Kind}) state={State}", info.Id, info.Kind, info.State);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Scheduled backup failed");
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var alerts = scope.ServiceProvider.GetRequiredService<IOperatorAlertService>();
                    await alerts.AlertAsync("Backup failed", ex.Message, CancellationToken.None);
                }
                catch
                {
                    // ignore secondary alert failures
                }
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
