using Channeled.Application.Chat;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Channeled.Infrastructure.Chat;

/// <summary>Removes soft-deleted chat messages past retention (after a short startup delay, then periodically).</summary>
public sealed class ChatPurgeHostedService(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<ChatOptions> options,
    ILogger<ChatPurgeHostedService> logger) : BackgroundService
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
                var purge = scope.ServiceProvider.GetRequiredService<IChatPurgeService>();
                var removed = await purge.PurgeExpiredAsync(stoppingToken);
                logger.LogInformation("Chat purge completed: removed={Removed}", removed);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Chat purge failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromHours(Math.Max(1, options.CurrentValue.PurgeIntervalHours)), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
