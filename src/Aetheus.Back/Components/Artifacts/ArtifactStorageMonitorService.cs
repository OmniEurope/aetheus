// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Aetheus.Back.Components.Artifacts;

/// <summary>
/// Measures the physical artifact volume, not only database metadata, and raises a throttled alert
/// when its configured budget or normalized growth trajectory is exceeded. It never deletes data.
/// </summary>
public sealed class ArtifactStorageMonitorService(
    IConfiguration configuration,
    IHubContext<AlertHub> alertHub,
    TimeProvider timeProvider,
    ILogger<ArtifactStorageMonitorService> logger) : BackgroundService
{
    private long? _previousBytes;
    private DateTime? _previousAt;
    private DateTime? _lastAlertAt;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        do
        {
            try
            {
                await EvaluateAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Could not monitor artifact storage");
            }

            var minutes = Math.Clamp(configuration.GetValue("ArtifactStorage:MonitorIntervalMinutes", 60), 5, 1440);
            await Task.Delay(TimeSpan.FromMinutes(minutes), stoppingToken).ConfigureAwait(false);
        } while (!stoppingToken.IsCancellationRequested);
    }

    internal async Task EvaluateAsync(CancellationToken ct)
    {
        var basePath = Path.GetFullPath(configuration["ArtifactStorage:BasePath"] ?? "./data/artifacts");
        var currentBytes = MeasureDirectoryBytes(basePath);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var budgetBytes = Math.Max(0, configuration.GetValue("ArtifactStorage:VolumeBudgetBytes", 53_687_091_200L));
        var growthWarningBytesPerDay = Math.Max(0, configuration.GetValue("ArtifactStorage:GrowthWarningBytesPerDay", 5_368_709_120L));

        double? normalizedGrowthPerDay = null;
        if (_previousBytes is { } previousBytes && _previousAt is { } previousAt && now > previousAt)
        {
            var elapsedDays = (now - previousAt).TotalDays;
            normalizedGrowthPerDay = Math.Max(0, currentBytes - previousBytes) / elapsedDays;
        }

        logger.LogInformation(
            "Artifact storage inventory: path={Path}, bytes={Bytes}, budgetBytes={BudgetBytes}, growthBytesPerDay={GrowthBytesPerDay}",
            basePath, currentBytes, budgetBytes, normalizedGrowthPerDay);

        var overBudget = budgetBytes > 0 && currentBytes >= budgetBytes;
        var growingTooFast = growthWarningBytesPerDay > 0 && normalizedGrowthPerDay >= growthWarningBytesPerDay;
        var alertCooldown = TimeSpan.FromHours(6);
        if ((overBudget || growingTooFast)
            && (_lastAlertAt is null || now - _lastAlertAt >= alertCooldown))
        {
            _lastAlertAt = now;
            var cause = overBudget ? "budget dépassé" : "croissance trop rapide";
            logger.LogWarning(
                "Artifact storage alert: {Cause}; bytes={Bytes}, budgetBytes={BudgetBytes}, growthBytesPerDay={GrowthBytesPerDay}",
                cause, currentBytes, budgetBytes, normalizedGrowthPerDay);
            await alertHub.Clients.Group(HubGroups.Alerts).SendAsync("AlertTriggered", new AlertTriggeredDto
            {
                RuleName = "ArtifactStorage",
                Metric = "ArtifactVolumeBytes",
                Severity = overBudget ? "Critical" : "Warning",
                Message = $"Stockage des artefacts : {cause} ({currentBytes} octets).",
                Threshold = overBudget ? budgetBytes : growthWarningBytesPerDay,
                TriggeredAt = now
            }, ct).ConfigureAwait(false);
        }

        _previousBytes = currentBytes;
        _previousAt = now;
    }

    internal static long MeasureDirectoryBytes(string path)
    {
        if (!Directory.Exists(path)) return 0;
        long total = 0;
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(path));
        while (pending.TryPop(out var directory))
        {
            foreach (var file in directory.EnumerateFiles())
            {
                if ((file.Attributes & FileAttributes.ReparsePoint) == 0)
                    total = checked(total + file.Length);
            }
            foreach (var child in directory.EnumerateDirectories())
            {
                if ((child.Attributes & FileAttributes.ReparsePoint) == 0)
                    pending.Push(child);
            }
        }
        return total;
    }
}
