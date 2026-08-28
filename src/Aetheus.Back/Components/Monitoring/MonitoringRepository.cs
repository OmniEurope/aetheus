// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Monitoring;

public class MonitoringRepository(AppDbContext db) : IMonitoringRepository
{
    public Task<int> CountRunningPipelinesAsync(List<int>? accessibleProjectIds = null, CancellationToken ct = default) =>
        CountRunningPipelinesAsync(accessibleProjectIds, [], ct);

    public Task<List<PipelineRun>> GetRecentRunsAsync(int count, List<int>? accessibleProjectIds = null, CancellationToken ct = default) =>
        GetRecentRunsAsync(count, accessibleProjectIds, [], ct);

    public async Task<List<Server>> GetAllServersAsync(List<int>? accessibleIds = null, CancellationToken ct = default)
    {
        var query = db.Servers.AsNoTracking().AsQueryable();
        if (accessibleIds is not null)
            query = query.Where(s => accessibleIds.Contains(s.Id));
        return await query.ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<int> CountPendingTasksAsync(List<int>? accessibleServerIds = null, CancellationToken ct = default)
    {
        var query = db.Tasks.AsNoTracking().Where(t => t.Status == TaskExecutionStatus.Pending);
        if (accessibleServerIds is not null)
            query = query.Where(t => accessibleServerIds.Contains(t.ServerId));
        return await query.CountAsync(ct).ConfigureAwait(false);
    }

    public async Task<int> CountRunningPipelinesAsync(List<int>? accessibleProjectIds, List<int>? accessiblePipelineIds, CancellationToken ct)
    {
        var query = FilterRunsByAccess(db.PipelineRuns.AsNoTracking(), accessibleProjectIds, accessiblePipelineIds)
            .Where(r => r.Status == PipelineStatus.Running);
        return await query.CountAsync(ct).ConfigureAwait(false);
    }

    public async Task<List<PipelineRun>> GetRecentRunsAsync(int count, List<int>? accessibleProjectIds, List<int>? accessiblePipelineIds, CancellationToken ct)
    {
        return await FilterRunsByAccess(db.PipelineRuns.AsNoTracking(), accessibleProjectIds, accessiblePipelineIds)
            .Include(r => r.Pipeline)
            .Include(r => r.StepRuns)
            .OrderByDescending(r => r.StartedAt)
            .Take(count)
            .AsSplitQuery()
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<List<Project>> GetRecentProjectsAsync(int count, List<int>? accessibleProjectIds = null, CancellationToken ct = default)
    {
        var query = db.Projects.AsNoTracking().AsQueryable();
        if (accessibleProjectIds is not null)
            query = query.Where(p => accessibleProjectIds.Contains(p.Id));

        return await query
            .Include(p => p.Pipelines)
            .OrderByDescending(p => p.UpdatedAt)
            .Take(count)
            .AsSplitQuery()
            .ToListAsync(ct).ConfigureAwait(false);
    }

    private static IQueryable<PipelineRun> FilterRunsByAccess(
        IQueryable<PipelineRun> query,
        List<int>? accessibleProjectIds,
        List<int>? accessiblePipelineIds)
    {
        if (accessibleProjectIds is null || accessiblePipelineIds is null)
            return query;

        return query.Where(r =>
            accessiblePipelineIds.Contains(r.PipelineId)
            || (r.Pipeline.ProjectId.HasValue && accessibleProjectIds.Contains(r.Pipeline.ProjectId.Value))
            || (r.Pipeline.Environment != null
                && r.Pipeline.Environment.ProjectId.HasValue
                && accessibleProjectIds.Contains(r.Pipeline.Environment.ProjectId.Value))
            || (r.Pipeline.ProjectServer != null && accessibleProjectIds.Contains(r.Pipeline.ProjectServer.ProjectId)));
    }

    public async Task<List<ServerMetric>> GetServerMetricsSinceAsync(
        int serverId,
        DateTime since,
        CancellationToken ct = default,
        DateTime? afterUtc = null,
        int take = 1_000)
    {
        var query = db.ServerMetrics
            .AsNoTracking()
            .Where(m => m.ServerId == serverId
                && m.Timestamp >= since
                && (!afterUtc.HasValue || m.Timestamp > afterUtc.Value));

        // Select the newest bounded window in SQL, then restore chronological order for charts.
        var rows = await query
            .OrderByDescending(m => m.Timestamp)
            .Take(Math.Clamp(take, 1, 2_000))
            .ToListAsync(ct).ConfigureAwait(false);
        rows.Reverse();
        return rows;
    }
}
