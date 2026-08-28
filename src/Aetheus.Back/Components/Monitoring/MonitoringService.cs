// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.Monitoring;

public class MonitoringService(IMonitoringRepository repo, TimeProvider timeProvider) : IMonitoringService
{
    private const int RecentRunFetchLimit = 100;

    public Task<DashboardOverviewDto> GetDashboardAsync(
        List<int>? accessibleServerIds = null,
        List<int>? accessibleProjectIds = null,
        CancellationToken ct = default) =>
        GetDashboardCoreAsync(accessibleServerIds, accessibleProjectIds, [], includeDirectPipelineAccess: false, ct);

    public Task<DashboardOverviewDto> GetDashboardAsync(
        List<int>? accessibleServerIds,
        List<int>? accessibleProjectIds,
        List<int>? accessiblePipelineIds,
        CancellationToken ct) =>
        GetDashboardCoreAsync(accessibleServerIds, accessibleProjectIds, accessiblePipelineIds, includeDirectPipelineAccess: true, ct);

    private async Task<DashboardOverviewDto> GetDashboardCoreAsync(
        List<int>? accessibleServerIds,
        List<int>? accessibleProjectIds,
        List<int>? accessiblePipelineIds,
        bool includeDirectPipelineAccess,
        CancellationToken ct)
    {
        // DbContext is not thread-safe - execute sequentially.
        var servers = await repo.GetAllServersAsync(accessibleServerIds, ct).ConfigureAwait(false);
        var pendingTasks = await repo.CountPendingTasksAsync(accessibleServerIds, ct).ConfigureAwait(false);
        var runningPipelines = includeDirectPipelineAccess
            ? await repo.CountRunningPipelinesAsync(accessibleProjectIds, accessiblePipelineIds, ct).ConfigureAwait(false)
            : await repo.CountRunningPipelinesAsync(accessibleProjectIds, ct).ConfigureAwait(false);
        var recentRuns = includeDirectPipelineAccess
            ? await repo.GetRecentRunsAsync(RecentRunFetchLimit, accessibleProjectIds, accessiblePipelineIds, ct).ConfigureAwait(false)
            : await repo.GetRecentRunsAsync(RecentRunFetchLimit, accessibleProjectIds, ct).ConfigureAwait(false);
        var recentProjects = await repo.GetRecentProjectsAsync(10, accessibleProjectIds, ct).ConfigureAwait(false);

        return new DashboardOverviewDto
        {
            TotalServers = servers.Count,
            OnlineServers = servers.Count(s => s.Status == ServerStatus.Online),
            OfflineServers = servers.Count(s => s.Status == ServerStatus.Offline),
            PendingTasks = pendingTasks,
            RunningPipelines = runningPipelines,
            RecentRuns = recentRuns.Select(r => new PipelineRunDto
            {
                Id = r.Id,
                PipelineId = r.PipelineId,
                PipelineName = r.Pipeline?.Name ?? string.Empty,
                Status = r.Status,
                StartedAt = r.StartedAt,
                CompletedAt = r.CompletedAt,
                Steps = r.StepRuns.Select(s => new PipelineStepRunDto
                {
                    Id = s.Id,
                    StepName = s.StepName,
                    StageName = s.StageName,
                    Status = s.Status,
                    TriggeredRunId = s.TriggeredRunId
                }).ToList()
            }).ToList(),
            // Dashboard ordering: online servers first, then most-recently-active (LastHeartbeat) first,
            // so the operationally relevant boxes sit at the top of the tile.
            Servers = servers
                .OrderByDescending(s => s.Status == ServerStatus.Online)
                .ThenByDescending(s => s.LastHeartbeat)
                .Select(s => new ServerDto
                {
                    Id = s.Id,
                    Name = s.Name,
                    Hostname = s.Hostname,
                    OsDescription = s.OsDescription,
                    AgentVersion = s.AgentVersion,
                    Status = s.Status,
                    Type = s.Type,
                    LastHeartbeat = s.LastHeartbeat,
                    CreatedAt = s.CreatedAt,
                    OrganizationId = s.OrganizationId,
                    PipelineRunnerEnabled = s.PipelineRunnerEnabled,
                    DockerAvailable = s.DockerAvailable,
                    RequireContainerIsolation = s.RequireContainerIsolation,
                    InsecureTls = s.InsecureTls
                }).ToList(),
            Projects = recentProjects.Select(project =>
                ProjectDtoMapper.ToDto(project, includePipelineSummary: true)).ToList()
        };
    }

    public async Task<List<ServerMetricDto>> GetServerMetricsAsync(
        int serverId,
        int hours,
        CancellationToken ct = default,
        DateTime? afterUtc = null,
        int take = 1_000)
    {
        var since = timeProvider.GetUtcNow().UtcDateTime.AddHours(-hours);
        var metrics = await repo.GetServerMetricsSinceAsync(
            serverId,
            since,
            ct,
            afterUtc,
            take).ConfigureAwait(false);

        return metrics.Select(m => new ServerMetricDto
        {
            ServerId = m.ServerId,
            CpuPercent = m.CpuPercent,
            MemoryUsedMb = m.MemoryUsedMb,
            MemoryTotalMb = m.MemoryTotalMb,
            DiskUsedGb = m.DiskUsedGb,
            DiskTotalGb = m.DiskTotalGb,
            BuildCacheAvailable = m.BuildCacheAvailable,
            DockerInventoryAvailable = m.DockerInventoryAvailable,
            BuildCacheBytes = m.BuildCacheBytes,
            BuildCacheReclaimableBytes = m.BuildCacheReclaimableBytes,
            DockerImagesBytes = m.DockerImagesBytes,
            DockerContainersBytes = m.DockerContainersBytes,
            DockerVolumesBytes = m.DockerVolumesBytes,
            AgentWorkDirectoryBytes = m.AgentWorkDirectoryBytes,
            AgentInstallDirectoryBytes = m.AgentInstallDirectoryBytes,
            NuGetCacheBytes = m.NuGetCacheBytes,
            JournalBytes = m.JournalBytes,
            StorageMaintenanceDryRun = m.StorageMaintenanceDryRun,
            DeploymentOnly = m.DeploymentOnly,
            BuildActive = m.BuildActive,
            LastBuildAttemptAtUtc = m.LastBuildAttemptAtUtc,
            Timestamp = m.Timestamp
        }).ToList();
    }
}
