// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Servers;

namespace Aetheus.Back.Services;

public sealed class MetricsCleanupService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<MetricsCleanupService> logger,
    TimeProvider timeProvider) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(6));

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await CleanupExpiredMetricsAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during metrics cleanup");
            }
        }
    }

    internal async Task CleanupExpiredMetricsAsync(CancellationToken ct)
    {
        var retentionDays = Math.Clamp(configuration.GetValue("Metrics:RetentionDays", 30), 1, 3650);
        await using var scope = scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IServerRepository>();
        var cutoff = timeProvider.GetUtcNow().UtcDateTime.AddDays(-retentionDays);
        var deleted = await repo.DeleteMetricsOlderThanAsync(cutoff, ct).ConfigureAwait(false);

        if (deleted > 0)
            logger.LogInformation("Deleted {Count} metrics older than {Days} days", deleted, retentionDays);
    }
}
