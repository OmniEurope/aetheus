// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Monitoring;

public interface IMonitoringRepository
{
    Task<List<Server>> GetAllServersAsync(List<int>? accessibleIds = null, CancellationToken ct = default);

    Task<int> CountPendingTasksAsync(List<int>? accessibleServerIds = null, CancellationToken ct = default);

    Task<int> CountRunningPipelinesAsync(List<int>? accessibleProjectIds = null, CancellationToken ct = default);
    Task<int> CountRunningPipelinesAsync(List<int>? accessibleProjectIds, List<int>? accessiblePipelineIds, CancellationToken ct);

    Task<List<PipelineRun>> GetRecentRunsAsync(int count, List<int>? accessibleProjectIds = null, CancellationToken ct = default);
    Task<List<PipelineRun>> GetRecentRunsAsync(int count, List<int>? accessibleProjectIds, List<int>? accessiblePipelineIds, CancellationToken ct);

    Task<List<Project>> GetRecentProjectsAsync(int count, List<int>? accessibleProjectIds = null, CancellationToken ct = default);

    Task<List<ServerMetric>> GetServerMetricsSinceAsync(
        int serverId,
        DateTime since,
        CancellationToken ct = default,
        DateTime? afterUtc = null,
        int take = 1_000);
}
