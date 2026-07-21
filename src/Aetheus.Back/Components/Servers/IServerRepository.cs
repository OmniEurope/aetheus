// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;

namespace Aetheus.Back.Components.Servers;

public interface IServerRepository
{
    Task<(List<Server> Items, int TotalCount)> GetServersPagedAsync(
        string? search, string? sortBy, bool sortDescending,
        ServerType? type, ServerStatus? status,
        int page, int pageSize, List<int>? accessibleIds = null, CancellationToken ct = default);

    /// <summary>
    /// Projected variant of <see cref="GetServersPagedAsync"/> that returns <see cref="ServerDto"/>
    /// directly via a <c>.Select()</c> projection, avoiding loading full entity graphs.
    /// </summary>
    Task<(List<ServerDto> Items, int TotalCount)> GetServersPagedProjectedAsync(
        string? search, string? sortBy, bool sortDescending,
        ServerType? type, ServerStatus? status,
        int page, int pageSize, List<int>? accessibleIds = null, CancellationToken ct = default);

    Task<Server?> GetServerDetailAsync(int id, CancellationToken ct = default);

    /// <summary>S-TECH-N8R3: metric-report timestamps for a server at/after <paramref name="since"/>
    /// (oldest→newest) - the raw heartbeat history the detail view buckets into uptime% + a sparkline.</summary>
    Task<List<DateTime>> GetRecentMetricTimestampsAsync(int serverId, DateTime since, CancellationToken ct = default);

    // Backward-compat for tests still calling the old method name.
    Task<Server?> GetServerWithCollectionsAsync(int id, CancellationToken ct = default);

    Task<Server?> FindServerAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Loads the server together with its tokens - used by the diagnostic
    /// endpoint to inspect token validity without re-issuing a second query.
    /// </summary>
    Task<Server?> FindServerWithTokensAsync(int id, CancellationToken ct = default);

    Task RemoveServerAsync(Server server, CancellationToken ct = default);

    /// <remarks>Stages only - call <see cref="SaveChangesAsync"/> to persist.</remarks>
    Task AddMetricAsync(ServerMetric metric, CancellationToken ct = default);

    Task<(int PreviousBlockedCount, int PreviousWarningCount)> GetSecurityCountersAsync(int serverId, CancellationToken ct = default);

    Task ReplaceServicesAsync(int serverId, List<ServiceInfo> services, CancellationToken ct = default);

    Task ReplaceDockerContainersAsync(int serverId, List<DockerContainer> containers, CancellationToken ct = default);

    Task ReplaceDockerImagesAsync(int serverId, List<DockerImage> images, CancellationToken ct = default);

    Task ReplaceDockerComposeStacksAsync(int serverId, List<DockerComposeStack> stacks, CancellationToken ct = default);

    Task ReplaceDockerNetworksAsync(int serverId, List<DockerNetwork> networks, CancellationToken ct = default);

    Task ReplaceDockerVolumesAsync(int serverId, List<DockerVolume> volumes, CancellationToken ct = default);

    Task ReplaceApacheDataAsync(int serverId, ApacheState? state, List<ApacheModule> modules, List<ApacheVirtualHost> vhosts, CancellationToken ct = default);

    Task ReplaceCertbotCertificatesAsync(int serverId, List<CertbotCertificate> certificates, CancellationToken ct = default);

    Task ReplaceMailDataAsync(int serverId, MailState? state, CancellationToken ct = default);

    Task ReplaceTeamspeakDataAsync(
        int serverId, TeamspeakState? state, List<TeamspeakChannel> channels,
        List<TeamspeakClient> clients, List<TeamspeakBan> bans,
        CancellationToken ct = default);

    Task ReplacePortsentryDataAsync(int serverId, PortsentryState? state, List<PortsentryBlockedIp> blockedIps, CancellationToken ct = default);

    Task ReplaceRkhunterDataAsync(int serverId, RkhunterState? state, List<RkhunterWarning> warnings, CancellationToken ct = default);

    Task ReplaceSecurityUpdatesDataAsync(int serverId, SecurityUpdatesState? state, CancellationToken ct = default);
    Task<SecurityUpdatesState?> GetSecurityUpdatesStateAsync(int serverId, CancellationToken ct = default);
    Task ReplaceFirewallDataAsync(int serverId, FirewallState? state, CancellationToken ct = default);
    Task<FirewallState?> GetFirewallStateAsync(int serverId, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);

    Task<bool> ServerExistsAsync(int serverId, CancellationToken ct = default);

    Task<List<Server>> GetStaleOnlineServersAsync(TimeSpan threshold, CancellationToken ct = default);

    Task<int> DeleteMetricsOlderThanAsync(DateTime cutoff, CancellationToken ct = default);

    /// <summary>
    /// Returns metric (=heartbeat) timestamps for <paramref name="serverId"/>
    /// at or after <paramref name="since"/>, ascending. One row per heartbeat
    /// already exists in <c>ServerMetrics</c>; no separate table is needed.
    /// </summary>
    Task<IReadOnlyList<DateTime>> GetMetricTimestampsAsync(int serverId, DateTime since, CancellationToken ct = default);

    Task<List<string>> GetServerNamesAsync(List<int>? accessibleIds = null, CancellationToken ct = default);

    Task<List<string>> GetServerNamesAsync(CancellationToken ct = default);

    /// <summary>
    /// Lightweight id/name projection for bulk operations (e.g. "update all agents").
    /// When <paramref name="accessibleIds"/> is non-null, the result is restricted to those ids.
    /// </summary>
    Task<List<(int Id, string Name)>> GetServerIdNamePairsAsync(List<int>? accessibleIds = null, CancellationToken ct = default);

    Task<List<int>> GetProjectIdsForServerAsync(int serverId, CancellationToken ct = default);

    Task<List<int>> GetPipelineIdsForServerAsync(int serverId, CancellationToken ct = default);

    Task<List<Project>> GetProjectsForServerAsync(int serverId, CancellationToken ct = default);
    Task<(List<Project> Items, int Total)> GetProjectsForServerPagedAsync(
        int serverId, string? search, int page, int pageSize, string? sortBy, bool sortDescending,
        CancellationToken ct = default);

    Task<List<Pipeline>> GetPipelinesForServerAsync(int serverId, CancellationToken ct = default);

    Task<List<VariableLibrary>> GetVariableLibrariesForServerAsync(int serverId, CancellationToken ct = default);

    Task<List<Vault>> GetVaultsForServerAsync(int serverId, CancellationToken ct = default);

    Task<List<Release>> GetReleasesForServerAsync(int serverId, CancellationToken ct = default);

    Task<ServiceType?> GetServiceTypeAsync(int serverId, string serviceName, CancellationToken ct = default);
    Task AddTaskAsync(ServerTask task, CancellationToken ct = default);
    Task AddTasksAsync(IEnumerable<ServerTask> tasks, CancellationToken ct = default);
    Task<(List<ServerTask> Items, int TotalCount)> GetTasksPagedAsync(int serverId, int page, int pageSize, CancellationToken ct = default);

    Task<(List<TaskLog> Items, int TotalCount)> GetLogsPagedAsync(int serverId, int page, int pageSize, CancellationToken ct = default);
}
