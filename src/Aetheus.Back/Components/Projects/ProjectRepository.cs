// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Projects;

public class ProjectRepository(
    AppDbContext db,
    IProjectAnalysisGradeReader analysisGrades) : IProjectRepository
{
    public async Task<(List<Project> Items, int TotalCount)> GetProjectsPagedAsync(
        string? search, string? sortBy, bool sortDescending,
        int page, int pageSize, ProjectStatus? status = null, List<int>? accessibleIds = null, CancellationToken ct = default)
    {
        var query = db.Projects.AsNoTracking().AsQueryable();

        if (accessibleIds is not null)
            query = query.Where(p => accessibleIds.Contains(p.Id));

        if (status.HasValue)
            query = query.Where(p => p.Status == status.Value);

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(p => p.Name.Contains(search) || p.Description.Contains(search));

        var totalCount = await query.CountAsync(ct).ConfigureAwait(false);

        query = sortBy?.ToLowerInvariant() switch
        {
            "name" => sortDescending ? query.OrderByDescending(p => p.Name) : query.OrderBy(p => p.Name),
            "status" => sortDescending ? query.OrderByDescending(p => p.Status) : query.OrderBy(p => p.Status),
            "createdat" => sortDescending ? query.OrderByDescending(p => p.CreatedAt) : query.OrderBy(p => p.CreatedAt),
            _ => query.OrderBy(p => p.Name)
        };

        var items = await query
            .Include(p => p.Pipelines)
                .ThenInclude(pl => pl.Runs.OrderByDescending(r => r.StartedAt).Take(1))
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .AsSplitQuery()
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return (items, totalCount);
    }

    public async Task<Dictionary<int, DateTime?>> GetLastGitUpdatesAsync(
        IReadOnlyCollection<int> projectIds, CancellationToken ct = default)
    {
        if (projectIds.Count == 0) return [];

        // "Last git update" per project = the most recent of the internal repo's last push and any
        // external connection's last sync. Two id-scoped batched reads - no per-project N+1.
        var internalPushes = await db.GitInternalRepos.AsNoTracking()
            .Where(r => projectIds.Contains(r.ProjectId) && r.LastPushAt != null)
            .Select(r => new { r.ProjectId, Date = r.LastPushAt })
            .ToListAsync(ct).ConfigureAwait(false);

        var externalSyncs = await db.GitConnections.AsNoTracking()
            .Where(c => projectIds.Contains(c.ProjectId) && c.LastSyncedAt != null)
            .Select(c => new { c.ProjectId, Date = c.LastSyncedAt })
            .ToListAsync(ct).ConfigureAwait(false);

        return internalPushes.Concat(externalSyncs)
            .GroupBy(x => x.ProjectId)
            .ToDictionary(g => g.Key, g => g.Max(x => x.Date));
    }

    public async Task<Dictionary<int, int>> GetInternalRepositoryIdsAsync(
        IReadOnlyCollection<int> projectIds, CancellationToken ct = default)
    {
        if (projectIds.Count == 0) return [];

        return await db.GitInternalRepos.AsNoTracking()
            .Where(repository => projectIds.Contains(repository.ProjectId))
            .GroupBy(repository => repository.ProjectId)
            .Select(group => new { ProjectId = group.Key, RepositoryId = group.Min(repository => repository.Id) })
            .ToDictionaryAsync(item => item.ProjectId, item => item.RepositoryId, ct)
            .ConfigureAwait(false);
    }

    public async Task<Dictionary<int, ProjectListInsight>> GetProjectListInsightsAsync(
        IReadOnlyCollection<int> projectIds,
        DateTime activeSessionCutoffUtc,
        CancellationToken ct = default)
    {
        if (projectIds.Count == 0) return [];

        var latestCommits = await db.GitCommits
            .AsNoTracking()
            .Where(commit => projectIds.Contains(commit.ProjectId))
            .GroupBy(commit => commit.ProjectId)
            .Select(group => group
                .OrderByDescending(commit => commit.CommittedAt ?? commit.CreatedAt)
                .ThenByDescending(commit => commit.Id)
                .Select(commit => new
                {
                    commit.ProjectId,
                    commit.Id,
                    commit.Sha,
                    commit.Message,
                    Date = commit.CommittedAt ?? commit.CreatedAt
                })
                .First())
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var latestRuns = await db.PipelineRuns
            .AsNoTracking()
            .Where(run => run.Pipeline.ProjectId.HasValue
                && projectIds.Contains(run.Pipeline.ProjectId.Value))
            .GroupBy(run => run.Pipeline.ProjectId!.Value)
            .Select(group => group
                .OrderByDescending(run => run.StartedAt)
                .ThenByDescending(run => run.Id)
                .Select(run => new
                {
                    ProjectId = run.Pipeline.ProjectId!.Value,
                    run.Id,
                    PipelineName = run.Pipeline.Name,
                    run.Status,
                    run.StartedAt,
                    ParentRunId = db.PipelineStepRuns
                        .Where(step => step.TriggeredRunId == run.Id)
                        .OrderBy(step => step.Id)
                        .Select(step => (int?)step.PipelineRunId)
                        .FirstOrDefault(),
                    ParentRunName = db.PipelineStepRuns
                        .Where(step => step.TriggeredRunId == run.Id)
                        .OrderBy(step => step.Id)
                        .Select(step => step.PipelineRun.Pipeline.Name)
                        .FirstOrDefault()
                })
                .First())
            .ToListAsync(ct)
            .ConfigureAwait(false);

        // Use the same cross-run, latest-domain grade as the project Quality page. Looking only at
        // the newest gate run is incorrect when quality and security are evaluated by separate
        // pipelines: the most recent run may be A while another current required domain is F.
        var gradesByProject = await analysisGrades.GetGradesAsync(projectIds, ct)
            .ConfigureAwait(false);

        var monitoredApps = await db.MonitoredApps
            .AsNoTracking()
            .Where(app => app.Enabled && projectIds.Contains(app.ProjectId))
            .Select(app => new
            {
                app.Id,
                app.ProjectId,
                app.CurrentStatus,
                app.AnalyticsEnabled,
                EnvironmentType = app.Environment == null
                    ? (EnvironmentType?)null
                    : app.Environment.Type,
                ActiveSessionCount = db.AppAnalyticsSessions
                    .Where(session => session.MonitoredAppId == app.Id
                        && session.LastSeenAtUtc >= activeSessionCutoffUtc)
                    .Select(session => session.SessionPseudonym)
                    .Distinct()
                    .Count()
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var productionAppsByProject = monitoredApps
            .GroupBy(app => app.ProjectId)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var explicitlyProduction = group
                        .Where(app => app.EnvironmentType == EnvironmentType.Production)
                        .ToList();
                    return explicitlyProduction.Count > 0
                        ? explicitlyProduction
                        : group.Where(app => app.EnvironmentType is null).ToList();
                });

        var commitsByProject = latestCommits.ToDictionary(commit => commit.ProjectId);
        var runsByProject = latestRuns.ToDictionary(run => run.ProjectId);
        var result = new Dictionary<int, ProjectListInsight>(projectIds.Count);

        foreach (var projectId in projectIds)
        {
            commitsByProject.TryGetValue(projectId, out var commit);
            runsByProject.TryGetValue(projectId, out var run);
            var productionApps = productionAppsByProject.GetValueOrDefault(projectId) ?? [];
            var productionStatus = ResolveProductionStatus(productionApps.Select(app => app.CurrentStatus));
            var analyticsAvailable = productionApps.Any(app => app.AnalyticsEnabled);

            result[projectId] = new ProjectListInsight(
                projectId,
                commit?.Id,
                commit?.Sha,
                commit?.Message,
                commit?.Date,
                run?.Id,
                run?.PipelineName,
                run?.Status,
                run?.StartedAt,
                run?.ParentRunId,
                run?.ParentRunName,
                gradesByProject.GetValueOrDefault(projectId)?.OverallGrade,
                productionStatus,
                analyticsAvailable
                    ? productionApps.Where(app => app.AnalyticsEnabled).Sum(app => app.ActiveSessionCount)
                    : null);
        }

        return result;
    }

    private static ProjectProductionStatus ResolveProductionStatus(IEnumerable<AppHealthStatus> statuses)
    {
        var values = statuses.ToList();
        if (values.Count == 0 || values.Any(status => status == AppHealthStatus.Unknown))
            return ProjectProductionStatus.Unavailable;
        if (values.Any(status => status is AppHealthStatus.Down or AppHealthStatus.Degraded))
            return ProjectProductionStatus.Offline;
        return values.All(status => status == AppHealthStatus.Up)
            ? ProjectProductionStatus.Online
            : ProjectProductionStatus.Unavailable;
    }

    public async Task<Project?> GetProjectDetailAsync(int id, CancellationToken ct = default)
    {
        return await db.Projects
            .AsNoTracking()
            .Where(p => p.Id == id)
            .Include(p => p.Pipelines)
                .ThenInclude(pl => pl.Runs.OrderByDescending(r => r.StartedAt).Take(10))
                    .ThenInclude(r => r.StepRuns.Where(s => !s.IsSystem && s.ServerId != null).OrderBy(s => s.Order).Take(1))
                        .ThenInclude(s => s.Server)
            .AsSplitQuery()
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<Dictionary<int, string>> GetActiveRunStepLabelsAsync(int projectId, CancellationToken ct = default)
    {
        var candidates = await db.PipelineStepRuns
            .AsNoTracking()
            .Where(step => step.PipelineRun.Pipeline.ProjectId == projectId
                && (step.PipelineRun.Status == PipelineStatus.Running
                    || step.PipelineRun.Status == PipelineStatus.Pending
                    || step.PipelineRun.Status == PipelineStatus.WaitingForApproval)
                && (step.Status == TaskExecutionStatus.Running
                    || step.Status == TaskExecutionStatus.Assigned
                    || (step.Status == TaskExecutionStatus.Pending && !step.IsSystem)))
            .Select(step => new
            {
                step.PipelineRunId,
                step.StageName,
                step.StepName,
                step.Status,
                step.Order
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return candidates
            .GroupBy(step => step.PipelineRunId)
            .Select(group => group
                .OrderBy(step => step.Status == TaskExecutionStatus.Running ? 0
                    : step.Status == TaskExecutionStatus.Assigned ? 1 : 2)
                .ThenBy(step => step.Order)
                .First())
            .ToDictionary(
                step => step.PipelineRunId,
                step => $"{NormalizeStageName(step.StageName)} · {step.StepName}");
    }

    private static string NormalizeStageName(string stageName) =>
        stageName.StartsWith("System:", StringComparison.Ordinal)
            ? stageName["System:".Length..]
            : stageName;

    public async Task<ProjectSectionCounts> GetProjectSectionCountsAsync(int projectId, CancellationToken ct = default)
    {
        // S-TECH-N8R3: independent COUNT queries (cheap, indexed) rather than loading the collections.
        var servers = await db.ProjectServers.CountAsync(ps => ps.ProjectId == projectId, ct).ConfigureAwait(false);
        var releases = await db.Releases.CountAsync(r => r.ProjectId == projectId, ct).ConfigureAwait(false);
        var vaults = await db.Vaults.CountAsync(v => v.ProjectId == projectId, ct).ConfigureAwait(false);
        var libraries = await db.VariableLibraries.CountAsync(v => v.ProjectId == projectId, ct).ConfigureAwait(false);
        var environments = await db.Environments.CountAsync(e => e.ProjectId == projectId, ct).ConfigureAwait(false);
        return new ProjectSectionCounts(servers, releases, vaults, libraries, environments);
    }

    public async Task<Project?> FindProjectWithPipelinesAsync(int id, CancellationToken ct = default)
    {
        return await db.Projects
            .Where(p => p.Id == id)
            .Include(p => p.Pipelines)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<Project?> FindProjectAsync(int id, CancellationToken ct = default)
    {
        return await db.Projects.FindAsync([id], ct).ConfigureAwait(false);
    }

    public async Task AddProjectAsync(Project project, CancellationToken ct = default)
    {
        db.Projects.Add(project);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task RemoveProjectAsync(Project project, CancellationToken ct = default)
    {
        db.Projects.Remove(project);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<List<Project>> GetAllProjectsWithRepoUrlAsync(CancellationToken ct = default)
    {
        return await db.Projects
            .AsNoTracking()
            .Where(p => p.RepositoryUrl != null && p.RepositoryUrl != "")
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<List<Server>> GetServersForProjectAsync(int projectId, CancellationToken ct = default)
    {
        var serverIds = await db.PipelineStepRuns
            .Where(psr => psr.PipelineRun.Pipeline.ProjectId == projectId && psr.ServerId != null)
            .Select(psr => psr.ServerId!.Value)
            .Distinct()
            .ToListAsync(ct).ConfigureAwait(false);

        return await db.Servers
            .Where(s => serverIds.Contains(s.Id))
            .AsNoTracking()
            .OrderBy(s => s.Name)
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<List<ProjectServer>> GetProjectServersAsync(int projectId, CancellationToken ct = default)
    {
        return await db.ProjectServers
            .Where(ps => ps.ProjectId == projectId)
            .Include(ps => ps.Server)
            .AsNoTracking()
            .OrderBy(ps => ps.DisplayName)
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<(List<ProjectServer> Items, int TotalCount)> GetProjectServersPageAsync(
        int projectId, string? search, string? sortBy, bool sortDescending,
        int page, int pageSize, CancellationToken ct = default)
    {
        var query = db.ProjectServers
            .Where(projectServer => projectServer.ProjectId == projectId)
            .AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalized = search.Trim().ToLowerInvariant();
            query = query.Where(projectServer =>
                projectServer.DisplayName.ToLower().Contains(normalized)
                || (projectServer.Host ?? string.Empty).ToLower().Contains(normalized)
                || (projectServer.Server != null
                    && projectServer.Server.Name.ToLower().Contains(normalized)));
        }

        query = query.Include(projectServer => projectServer.Server);
        var totalCount = await query.CountAsync(ct).ConfigureAwait(false);
        query = (sortBy, sortDescending) switch
        {
            ("Host", false) => query.OrderBy(item => item.Host),
            ("Host", true) => query.OrderByDescending(item => item.Host),
            ("Port", false) => query.OrderBy(item => item.Port),
            ("Port", true) => query.OrderByDescending(item => item.Port),
            ("Type", false) => query.OrderBy(item => item.Type),
            ("Type", true) => query.OrderByDescending(item => item.Type),
            ("DisplayName", true) => query.OrderByDescending(item => item.DisplayName),
            _ => query.OrderBy(item => item.DisplayName)
        };
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(ct).ConfigureAwait(false);
        return (items, totalCount);
    }

    public async Task<ProjectServer?> GetProjectServerAsync(int projectId, int projectServerId, CancellationToken ct = default)
    {
        return await db.ProjectServers
            .Where(ps => ps.ProjectId == projectId && ps.Id == projectServerId)
            .Include(ps => ps.Server)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
    }

    public async Task AddProjectServerAsync(ProjectServer projectServer, CancellationToken ct = default)
    {
        db.ProjectServers.Add(projectServer);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task RemoveProjectServerAsync(ProjectServer projectServer, CancellationToken ct = default)
    {
        db.ProjectServers.Remove(projectServer);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<(List<ServerTask> Items, int TotalCount)> GetTasksPagedAsync(
        int projectId, string? search, string? sortBy, bool sortDescending,
        int page, int pageSize, CancellationToken ct = default)
    {
        var query = db.Tasks
            .Where(t => t.PipelineRun != null && t.PipelineRun.Pipeline.ProjectId == projectId)
            .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalized = search.Trim().ToLowerInvariant();
            query = query.Where(task => task.Name.ToLower().Contains(normalized)
                || (task.Server != null && task.Server.Name.ToLower().Contains(normalized)));
        }

        query = query.Include(task => task.Server);
        var totalCount = await query.CountAsync(ct).ConfigureAwait(false);
        query = (sortBy, sortDescending) switch
        {
            ("Name", false) => query.OrderBy(task => task.Name),
            ("Name", true) => query.OrderByDescending(task => task.Name),
            ("ServerName", false) => query.OrderBy(task => task.Server!.Name),
            ("ServerName", true) => query.OrderByDescending(task => task.Server!.Name),
            ("Status", false) => query.OrderBy(task => task.Status),
            ("Status", true) => query.OrderByDescending(task => task.Status),
            ("ExitCode", false) => query.OrderBy(task => task.ExitCode),
            ("ExitCode", true) => query.OrderByDescending(task => task.ExitCode),
            ("CreatedAt", false) => query.OrderBy(task => task.CreatedAt),
            _ => query.OrderByDescending(task => task.CreatedAt)
        };
        var items = await query.Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct).ConfigureAwait(false);

        return (items, totalCount);
    }

    public async Task<(List<TaskLog> Items, int TotalCount)> GetLogsPagedAsync(
        int projectId, string? search, string? sortBy, bool sortDescending,
        int page, int pageSize, CancellationToken ct = default)
    {
        var query = db.TaskLogs
            .Where(l => l.Task.PipelineRun != null && l.Task.PipelineRun.Pipeline.ProjectId == projectId)
            .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalized = search.Trim().ToLowerInvariant();
            query = query.Where(log => log.Message.ToLower().Contains(normalized));
        }

        var totalCount = await query.CountAsync(ct).ConfigureAwait(false);
        query = (sortBy, sortDescending) switch
        {
            ("Level", false) => query.OrderBy(log => log.Level),
            ("Level", true) => query.OrderByDescending(log => log.Level),
            ("Message", false) => query.OrderBy(log => log.Message),
            ("Message", true) => query.OrderByDescending(log => log.Message),
            ("Timestamp", false) => query.OrderBy(log => log.Timestamp),
            _ => query.OrderByDescending(log => log.Timestamp)
        };
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct).ConfigureAwait(false);

        return (items, totalCount);
    }

    public async Task<List<ServerTask>> GetRecentTasksAsync(int projectId, int count, CancellationToken ct = default)
    {
        return await db.Tasks
            .Where(t => t.PipelineRun != null && t.PipelineRun.Pipeline.ProjectId == projectId)
            .AsNoTracking()
            .Include(t => t.Server)
            .OrderByDescending(t => t.CreatedAt)
            .Take(count)
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<List<PipelineRun>> GetRecentPipelineRunsAsync(int projectId, int count, CancellationToken ct = default)
    {
        return await db.PipelineRuns
            .Where(r => r.Pipeline.ProjectId == projectId)
            .Include(r => r.Pipeline)
            .AsNoTracking()
            .OrderByDescending(r => r.StartedAt)
            .Take(count)
            .ToListAsync(ct).ConfigureAwait(false);
    }
}
