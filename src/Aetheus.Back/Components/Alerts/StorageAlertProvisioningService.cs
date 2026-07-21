// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Servers;
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.Enums;

namespace Aetheus.Back.Components.Alerts;

/// <summary>
/// Ensures every enrolled server has the three baseline storage guards. Existing rules are never
/// re-enabled or overwritten, so an operator can still disable or customize a provisioned rule.
/// </summary>
public sealed class StorageAlertProvisioningService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<StorageAlertProvisioningService> logger) : BackgroundService
{
    private static readonly TimeSpan ReconcileInterval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        do
        {
            try
            {
                await ProvisionAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Could not provision baseline storage alert rules");
            }

            await Task.Delay(ReconcileInterval, stoppingToken).ConfigureAwait(false);
        } while (!stoppingToken.IsCancellationRequested);
    }

    internal async Task ProvisionAsync(CancellationToken ct)
    {
        if (!configuration.GetValue("StorageMonitoring:ProvisionDefaultAlerts", true)) return;

        var warningPercent = Math.Clamp(configuration.GetValue("StorageMonitoring:DiskWarningPercent", 80d), 1d, 100d);
        var criticalPercent = Math.Clamp(configuration.GetValue("StorageMonitoring:DiskCriticalPercent", 90d), warningPercent, 100d);
        var minimumFreeGiB = Math.Max(1d, configuration.GetValue("StorageMonitoring:MinFreeGiB", 20d));

        await using var scope = scopeFactory.CreateAsyncScope();
        var alertRepository = scope.ServiceProvider.GetRequiredService<IAlertRepository>();
        var serverRepository = scope.ServiceProvider.GetRequiredService<IServerRepository>();
        var servers = await serverRepository.GetServerIdNamePairsAsync(ct: ct).ConfigureAwait(false);
        var existingRules = await alertRepository.GetAllAsync(ct).ConfigureAwait(false);
        var existingProvisioningKeys = existingRules
            .Select(rule => rule.ProvisioningKey)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        var addedRules = new List<AlertRule>();

        foreach (var server in servers)
        {
            AddIfMissing(existingProvisioningKeys, addedRules, $"storage-disk-warning:{server.Id}", server.Id,
                $"Stockage ≥ {warningPercent:0.#} % - {server.Name}",
                MetricType.Disk, ComparisonOperator.GreaterThanOrEqual, warningPercent, 300, AlertSeverity.Warning);
            AddIfMissing(existingProvisioningKeys, addedRules, $"storage-disk-critical:{server.Id}", server.Id,
                $"Stockage ≥ {criticalPercent:0.#} % - {server.Name}",
                MetricType.Disk, ComparisonOperator.GreaterThanOrEqual, criticalPercent, 60, AlertSeverity.Critical);
            AddIfMissing(existingProvisioningKeys, addedRules, $"storage-disk-free:{server.Id}", server.Id,
                $"Stockage libre < {minimumFreeGiB:0.#} Gio - {server.Name}",
                MetricType.DiskFree, ComparisonOperator.LessThan, minimumFreeGiB, 60, AlertSeverity.Critical);
        }

        if (addedRules.Count == 0) return;
        await alertRepository.AddRangeAsync(addedRules, ct).ConfigureAwait(false);
        logger.LogInformation("Provisioned {Count} baseline storage alert rule(s)", addedRules.Count);
    }

    private static void AddIfMissing(
        ISet<string> existingProvisioningKeys,
        ICollection<AlertRule> addedRules,
        string provisioningKey,
        int serverId,
        string name,
        MetricType metric,
        ComparisonOperator comparison,
        double threshold,
        int sustainedSeconds,
        AlertSeverity severity)
    {
        if (!existingProvisioningKeys.Add(provisioningKey)) return;

        addedRules.Add(new AlertRule
        {
            Name = name,
            ProvisioningKey = provisioningKey,
            ServerId = serverId,
            Metric = metric,
            Operator = comparison,
            Threshold = threshold,
            SustainedSeconds = sustainedSeconds,
            Severity = severity,
            IsEnabled = true
        });
    }
}
