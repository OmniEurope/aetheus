// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.AppMonitoring;

public interface IAppMonitoringRepository
{
    Task<List<MonitoredApp>> GetAppsByProjectAsync(int projectId, CancellationToken ct = default);
    Task<MonitoredApp?> GetAppAsync(int id, CancellationToken ct = default);
    Task<MonitoredApp?> GetAppForUpdateAsync(int id, CancellationToken ct = default);
    Task<int?> GetAppProjectIdAsync(int id, CancellationToken ct = default);

    /// <summary>Maps each project id to its OrganizationId (for per-org SignalR broadcast).</summary>
    Task<Dictionary<int, int>> GetProjectOrgIdsAsync(IReadOnlyCollection<int> projectIds, CancellationToken ct = default);

    /// <summary>Apps monitored by black-box backend probing (off-fleet: ServerId is null, URL set, enabled).</summary>
    Task<List<MonitoredApp>> GetBackendProbedAppsAsync(CancellationToken ct = default);

    /// <summary>Enabled probe configs for the apps hosted by a given fleet server.</summary>
    Task<List<MonitoredApp>> GetAppsForServerAsync(int serverId, CancellationToken ct = default);

    /// <summary>Maps each app id to its owning ServerId (null when off-fleet) for the agent ownership guard.</summary>
    Task<Dictionary<int, int?>> GetAppServerIdsAsync(IReadOnlyCollection<int> appIds, CancellationToken ct = default);

    Task<Dictionary<int, MonitoredApp>> GetAppsByIdsForUpdateAsync(IReadOnlyCollection<int> appIds, CancellationToken ct = default);

    /// <summary>Status histogram across a set of accessible projects (null = all) for the dashboard tile.</summary>
    Task<List<MonitoredApp>> GetAppsForSummaryAsync(List<int>? accessibleProjectIds, CancellationToken ct = default);
    Task<long> GetTelemetryStorageBytesAsync(CancellationToken ct = default);

    Task AddAppAsync(MonitoredApp app, CancellationToken ct = default);
    Task RemoveAppAsync(MonitoredApp app, CancellationToken ct = default);
    Task AddSamplesAsync(IEnumerable<AppHealthSample> samples, CancellationToken ct = default);
    Task<List<AppHealthSample>> GetSamplesSinceAsync(int appId, DateTime since, CancellationToken ct = default);

    /// <summary>Raw uptime (up, total) from samples since a cutoff.</summary>
    Task<(int Up, int Total)> GetRawUptimeAsync(int appId, DateTime since, CancellationToken ct = default);

    /// <summary>Aggregated uptime (up, total) from hourly rollups since a cutoff.</summary>
    Task<(int Up, int Total)> GetHourlyUptimeAsync(int appId, DateTime since, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);

    // --- OTLP ingestion (phase 2+) ---
    /// <summary>Resolves an ingest key hash to its enabled app id (null if unknown/disabled).</summary>
    Task<int?> GetAppIdByIngestKeyHashAsync(string ingestKeyHash, CancellationToken ct = default);
    /// <summary>Records a successful ingest touch and increments the dropped counter (surfaced in UI).</summary>
    Task TouchIngestAsync(int appId, long droppedDelta, DateTime now, CancellationToken ct = default);

    /// <summary>Tracked enabled app to auto-instrument on deploy: prefers an environment match, then a
    /// project-level (null environment) app. Null when the project has no enabled monitored app.</summary>
    Task<MonitoredApp?> GetDeployTargetAppAsync(int projectId, int? environmentId, CancellationToken ct = default);

    // --- retention ---
    Task<int> AggregateRawIntoHourlyAsync(DateTime currentHourStartUtc, DateTime rawFloorUtc, CancellationToken ct = default);
    Task<int> PurgeRawOlderThanAsync(DateTime cutoff, CancellationToken ct = default);
    Task<int> PurgeHourlyOlderThanAsync(DateTime cutoff, CancellationToken ct = default);
}
