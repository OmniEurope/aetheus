// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Monitoring;

public interface IMonitoringRepository
{
    /// <summary>Recette R-480: the servers the dashboard tile lists, projected to the columns it shows and
    /// ordered online first, then most recently active.</summary>
    Task<List<ServerDto>> GetDashboardServersAsync(List<int>? accessibleIds = null, CancellationToken ct = default);

    Task<int> CountPendingTasksAsync(List<int>? accessibleServerIds = null, CancellationToken ct = default);

    Task<int> CountRunningPipelinesAsync(List<int>? accessibleProjectIds = null, CancellationToken ct = default);
    Task<int> CountRunningPipelinesAsync(List<int>? accessibleProjectIds, List<int>? accessiblePipelineIds, CancellationToken ct);

    /// <summary>Same run-list projection/hydration the pipeline run grids use (see
    /// <c>PipelineRunListRows.Projection</c>, recette R-263), so the dashboard shows the same GateGrade,
    /// Branch, Commit, Server and Project fields as every other run grid instead of a bespoke,
    /// partial mapping. Recette R-480: <c>groupCount</c> is the number of run groups the tile shows (a root
    /// run with the runs it triggered), not a number of rows.</summary>
    Task<List<PipelineRunDto>> GetRecentRunsAsync(int groupCount, List<int>? accessibleProjectIds = null, CancellationToken ct = default);
    Task<List<PipelineRunDto>> GetRecentRunsAsync(int groupCount, List<int>? accessibleProjectIds, List<int>? accessiblePipelineIds, CancellationToken ct);

    Task<List<Project>> GetRecentProjectsAsync(int count, List<int>? accessibleProjectIds = null, CancellationToken ct = default);

    /// <summary>Every project the caller can read, not only the recent ones listed on the dashboard.</summary>
    Task<int> CountProjectsAsync(List<int>? accessibleProjectIds = null, CancellationToken ct = default);

    Task<List<ServerMetric>> GetServerMetricsSinceAsync(
        int serverId,
        DateTime since,
        CancellationToken ct = default,
        DateTime? afterUtc = null,
        int take = 1_000);
}
