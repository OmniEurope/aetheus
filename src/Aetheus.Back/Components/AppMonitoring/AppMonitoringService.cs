// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Notifications;
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Exceptions;
using Aetheus.Back.Services;
using Aetheus.Shared.Constants;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;

namespace Aetheus.Back.Components.AppMonitoring;

public sealed class AppMonitoringService(
    IAppMonitoringRepository repo,
    INotificationService notificationService,
    IEntityChangeNotifier notifier,
    TimeProvider timeProvider,
    ILogger<AppMonitoringService> logger) : IAppMonitoringService
{
    public async Task<List<MonitoredAppDto>> GetAppsForProjectAsync(int projectId, CancellationToken ct = default)
    {
        var apps = await repo.GetAppsByProjectAsync(projectId, ct).ConfigureAwait(false);
        var result = new List<MonitoredAppDto>(apps.Count);
        foreach (var app in apps)
            result.Add(await MapWithUptimeAsync(app, ct).ConfigureAwait(false));
        return result;
    }

    public async Task<MonitoredAppDto?> GetAppAsync(int id, CancellationToken ct = default)
    {
        var app = await repo.GetAppAsync(id, ct).ConfigureAwait(false);
        return app is null ? null : await MapWithUptimeAsync(app, ct).ConfigureAwait(false);
    }

    public Task<int?> GetAppProjectIdAsync(int id, CancellationToken ct = default) =>
        repo.GetAppProjectIdAsync(id, ct);

    public async Task<MonitoredAppDto> CreateAppAsync(int projectId, CreateMonitoredAppRequest request, CancellationToken ct = default)
    {
        ValidateProbeUrl(request.ProbeUrl);

        var app = new MonitoredApp
        {
            ProjectId = projectId,
            EnvironmentId = request.EnvironmentId,
            ServerId = request.ServerId,
            Name = request.Name.Trim(),
            ProbeUrl = string.IsNullOrWhiteSpace(request.ProbeUrl) ? null : request.ProbeUrl.Trim(),
            ProbeIntervalSeconds = Math.Clamp(request.ProbeIntervalSeconds, AppMonitoringDefaults.MinimumProbeIntervalSeconds, AppMonitoringDefaults.MaximumProbeIntervalSeconds),
            ProbeTimeoutSeconds = Math.Clamp(request.ProbeTimeoutSeconds, AppMonitoringDefaults.MinimumProbeTimeoutSeconds, AppMonitoringDefaults.MaximumProbeTimeoutSeconds),
            ExpectedStatusCode = Math.Clamp(request.ExpectedStatusCode, AppMonitoringDefaults.MinimumExpectedStatusCode, AppMonitoringDefaults.MaximumExpectedStatusCode),
            FailureThreshold = Math.Clamp(request.FailureThreshold, AppMonitoringDefaults.MinimumTransitionThreshold, AppMonitoringDefaults.MaximumTransitionThreshold),
            RecoveryThreshold = Math.Clamp(request.RecoveryThreshold, AppMonitoringDefaults.MinimumTransitionThreshold, AppMonitoringDefaults.MaximumTransitionThreshold),
            Enabled = request.Enabled,
            CurrentStatus = AppHealthStatus.Unknown
        };

        await repo.AddAppAsync(app, ct).ConfigureAwait(false);
        await BroadcastAppChangeAsync(app.ProjectId, EntityChangeOps.Created, ct).ConfigureAwait(false);
        return await MapWithUptimeAsync(app, ct).ConfigureAwait(false);
    }

    public async Task<MonitoredAppDto?> UpdateAppAsync(int id, UpdateMonitoredAppRequest request, CancellationToken ct = default)
    {
        ValidateProbeUrl(request.ProbeUrl);

        var app = await repo.GetAppForUpdateAsync(id, ct).ConfigureAwait(false);
        if (app is null)
            return null;

        app.Name = request.Name.Trim();
        app.EnvironmentId = request.EnvironmentId;
        app.ServerId = request.ServerId;
        app.ProbeUrl = string.IsNullOrWhiteSpace(request.ProbeUrl) ? null : request.ProbeUrl.Trim();
        app.ProbeIntervalSeconds = Math.Clamp(request.ProbeIntervalSeconds, AppMonitoringDefaults.MinimumProbeIntervalSeconds, AppMonitoringDefaults.MaximumProbeIntervalSeconds);
        app.ProbeTimeoutSeconds = Math.Clamp(request.ProbeTimeoutSeconds, AppMonitoringDefaults.MinimumProbeTimeoutSeconds, AppMonitoringDefaults.MaximumProbeTimeoutSeconds);
        app.ExpectedStatusCode = Math.Clamp(request.ExpectedStatusCode, AppMonitoringDefaults.MinimumExpectedStatusCode, AppMonitoringDefaults.MaximumExpectedStatusCode);
        app.FailureThreshold = Math.Clamp(request.FailureThreshold, AppMonitoringDefaults.MinimumTransitionThreshold, AppMonitoringDefaults.MaximumTransitionThreshold);
        app.RecoveryThreshold = Math.Clamp(request.RecoveryThreshold, AppMonitoringDefaults.MinimumTransitionThreshold, AppMonitoringDefaults.MaximumTransitionThreshold);
        app.Enabled = request.Enabled;

        await repo.SaveChangesAsync(ct).ConfigureAwait(false);
        await BroadcastAppChangeAsync(app.ProjectId, EntityChangeOps.Updated, ct).ConfigureAwait(false);
        return await MapWithUptimeAsync(app, ct).ConfigureAwait(false);
    }

    public async Task<bool> DeleteAppAsync(int id, CancellationToken ct = default)
    {
        var app = await repo.GetAppForUpdateAsync(id, ct).ConfigureAwait(false);
        if (app is null)
            return false;

        var projectId = app.ProjectId;
        await repo.RemoveAppAsync(app, ct).ConfigureAwait(false);
        await BroadcastAppChangeAsync(projectId, EntityChangeOps.Deleted, ct).ConfigureAwait(false);
        return true;
    }

    public async Task<List<AppHealthSampleDto>> GetSamplesAsync(int id, int hours, CancellationToken ct = default)
    {
        var since = timeProvider.GetUtcNow().UtcDateTime.AddHours(-Math.Clamp(hours, AppMonitoringDefaults.MinimumHistoryHours, AppMonitoringDefaults.MaximumHistoryHours));
        var samples = await repo.GetSamplesSinceAsync(id, since, ct).ConfigureAwait(false);
        return samples.Select(s => new AppHealthSampleDto
        {
            Timestamp = s.Timestamp,
            IsUp = s.IsUp,
            ResponseTimeMs = s.ResponseTimeMs,
            StatusCode = s.StatusCode,
            Error = s.Error
        }).ToList();
    }

    public async Task<List<AppProbeConfigDto>> GetProbeConfigsForServerAsync(int serverId, CancellationToken ct = default)
    {
        var apps = await repo.GetAppsForServerAsync(serverId, ct).ConfigureAwait(false);
        return apps.Select(a => new AppProbeConfigDto
        {
            MonitoredAppId = a.Id,
            ProbeUrl = a.ProbeUrl ?? string.Empty,
            ProbeIntervalSeconds = a.ProbeIntervalSeconds,
            ProbeTimeoutSeconds = a.ProbeTimeoutSeconds,
            ExpectedStatusCode = a.ExpectedStatusCode
        }).ToList();
    }

    public Task<Dictionary<int, int?>> GetAppServerIdsAsync(IReadOnlyCollection<int> appIds, CancellationToken ct = default) =>
        repo.GetAppServerIdsAsync(appIds, ct);

    public async Task<int> IngestProbeResultsAsync(IReadOnlyCollection<AppProbeResultDto> results, CancellationToken ct = default)
    {
        if (results.Count == 0)
            return 0;

        var ids = results.Select(r => r.MonitoredAppId).Distinct().ToList();
        var apps = await repo.GetAppsByIdsForUpdateAsync(ids, ct).ConfigureAwait(false);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var samples = new List<AppHealthSample>(results.Count);
        var transitions = new List<(MonitoredApp App, AppHealthStatus Previous, AppHealthStatus Next)>();

        foreach (var result in results.OrderBy(r => r.Timestamp))
        {
            if (!apps.TryGetValue(result.MonitoredAppId, out var app))
                continue; // unknown app id in the batch: silently skipped (ownership already enforced upstream)

            var ts = result.Timestamp == default
                ? now
                : DateTime.SpecifyKind(result.Timestamp.ToUniversalTime(), DateTimeKind.Utc);

            samples.Add(new AppHealthSample
            {
                MonitoredAppId = app.Id,
                Timestamp = ts,
                IsUp = result.IsUp,
                ResponseTimeMs = result.ResponseTimeMs,
                StatusCode = result.StatusCode,
                Error = Truncate(result.Error, 500)
            });

            var previous = app.CurrentStatus;
            var transition = AppHealthStateMachine.Apply(
                previous, app.ConsecutiveFailures, app.ConsecutiveSuccesses,
                result.IsUp, app.FailureThreshold, app.RecoveryThreshold);

            app.ConsecutiveFailures = transition.ConsecutiveFailures;
            app.ConsecutiveSuccesses = transition.ConsecutiveSuccesses;
            app.LastCheckedAt = ts;
            app.LastResponseTimeMs = result.ResponseTimeMs;

            if (transition.Changed)
            {
                app.CurrentStatus = transition.Status;
                app.LastStatusChangeAt = ts;
                transitions.Add((app, previous, transition.Status));
            }
        }

        // Single save: samples and the tracked app mutations share the same DbContext.
        await repo.AddSamplesAsync(samples, ct).ConfigureAwait(false);

        if (transitions.Count > 0)
            await FanOutTransitionsAsync(transitions, ct).ConfigureAwait(false);

        return samples.Count;
    }

    public async Task<AppMonitoringSummaryDto> GetSummaryAsync(List<int>? accessibleProjectIds, CancellationToken ct = default)
    {
        if (accessibleProjectIds is { Count: 0 })
            return new AppMonitoringSummaryDto();

        var apps = await repo.GetAppsForSummaryAsync(accessibleProjectIds, ct).ConfigureAwait(false);
        var telemetryStorageBytes = await repo.GetTelemetryStorageBytesAsync(ct).ConfigureAwait(false);

        var troubled = apps
            .Where(a => a.CurrentStatus is AppHealthStatus.Down or AppHealthStatus.Degraded)
            .OrderByDescending(a => a.LastStatusChangeAt ?? DateTime.MinValue)
            .Take(10)
            .Select(a => new MonitoredAppStatusDto
            {
                Id = a.Id,
                Name = a.Name,
                ProjectId = a.ProjectId,
                ProjectName = a.Project?.Name,
                CurrentStatus = a.CurrentStatus,
                LastStatusChangeAt = a.LastStatusChangeAt
            })
            .ToList();

        return new AppMonitoringSummaryDto
        {
            TotalCount = apps.Count,
            UpCount = apps.Count(a => a.CurrentStatus == AppHealthStatus.Up),
            DownCount = apps.Count(a => a.CurrentStatus == AppHealthStatus.Down),
            DegradedCount = apps.Count(a => a.CurrentStatus == AppHealthStatus.Degraded),
            UnknownCount = apps.Count(a => a.CurrentStatus == AppHealthStatus.Unknown),
            TelemetryStorageBytes = telemetryStorageBytes,
            Troubled = troubled
        };
    }

    private async Task FanOutTransitionsAsync(
        List<(MonitoredApp App, AppHealthStatus Previous, AppHealthStatus Next)> transitions,
        CancellationToken ct)
    {
        var projectIds = transitions.Select(t => t.App.ProjectId).Distinct().ToList();
        var orgIds = await repo.GetProjectOrgIdsAsync(projectIds, ct).ConfigureAwait(false);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        foreach (var (app, previous, next) in transitions)
        {
            var orgId = orgIds.TryGetValue(app.ProjectId, out var oid) ? (int?)oid : null;
            await notifier.BroadcastAsync(ResourceType.Project, app.ProjectId, EntityChangeOps.Updated, ct, orgId)
                .ConfigureAwait(false);

            var eventType = next switch
            {
                AppHealthStatus.Down => "app.down",
                AppHealthStatus.Up when previous == AppHealthStatus.Down => "app.recovered",
                _ => null
            };

            if (eventType is null)
                continue;

            logger.LogInformation("Monitored app {AppId} ({AppName}) transitioned {Previous} -> {Next}",
                app.Id, app.Name, previous, next);

            await notificationService.SendEventAsync(eventType, new
            {
                AppId = app.Id,
                AppName = app.Name,
                app.ProjectId,
                Status = next.ToString(),
                PreviousStatus = previous.ToString(),
                At = now
            }, ct).ConfigureAwait(false);
        }
    }

    private async Task BroadcastAppChangeAsync(int projectId, string op, CancellationToken ct)
    {
        var orgIds = await repo.GetProjectOrgIdsAsync([projectId], ct).ConfigureAwait(false);
        var orgId = orgIds.TryGetValue(projectId, out var oid) ? (int?)oid : null;
        await notifier.BroadcastAsync(ResourceType.Project, projectId, op, ct, orgId).ConfigureAwait(false);
    }

    private async Task<MonitoredAppDto> MapWithUptimeAsync(MonitoredApp app, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var (up24, total24) = await repo.GetRawUptimeAsync(app.Id, now.AddHours(-24), ct).ConfigureAwait(false);
        var (up7, total7) = await repo.GetRawUptimeAsync(app.Id, now.AddDays(-7), ct).ConfigureAwait(false);
        var (up90, total90) = await repo.GetHourlyUptimeAsync(app.Id, now.AddDays(-90), ct).ConfigureAwait(false);

        return new MonitoredAppDto
        {
            Id = app.Id,
            ProjectId = app.ProjectId,
            ProjectName = app.Project?.Name,
            EnvironmentId = app.EnvironmentId,
            EnvironmentName = app.Environment?.Name,
            ServerId = app.ServerId,
            ServerName = app.Server?.Name,
            Name = app.Name,
            ProbeUrl = app.ProbeUrl,
            ProbeIntervalSeconds = app.ProbeIntervalSeconds,
            ProbeTimeoutSeconds = app.ProbeTimeoutSeconds,
            ExpectedStatusCode = app.ExpectedStatusCode,
            FailureThreshold = app.FailureThreshold,
            RecoveryThreshold = app.RecoveryThreshold,
            Enabled = app.Enabled,
            CurrentStatus = app.CurrentStatus,
            LastStatusChangeAt = app.LastStatusChangeAt,
            LastCheckedAt = app.LastCheckedAt,
            LastResponseTimeMs = app.LastResponseTimeMs,
            CreatedAt = app.CreatedAt,
            Uptime24h = total24 > 0 ? (double)up24 / total24 : null,
            Uptime7d = total7 > 0 ? (double)up7 / total7 : null,
            Uptime90d = total90 > 0 ? (double)up90 / total90 : null,
            HasIngestKey = app.IngestKeyHash is not null,
            IngestKeyCreatedAt = app.IngestKeyCreatedAt,
            IngestDroppedCount = app.IngestDroppedCount,
            LastIngestAt = app.LastIngestAt
        };
    }

    private static void ValidateProbeUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new BadRequestException("Probe URL must be an absolute http(s) URL.");
    }

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];
}
