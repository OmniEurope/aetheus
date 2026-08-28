// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Releases;

public class ReleaseRepository(AppDbContext db) : IReleaseRepository
{
    public async Task<(List<Release> Items, int TotalCount)> GetReleasesPagedAsync(
        string? search, int? projectId, int page, int pageSize, List<int>? accessibleIds = null, CancellationToken ct = default,
        string? sortBy = null, bool sortDescending = true)
    {
        var query = db.Releases.AsNoTracking().AsQueryable();

        if (accessibleIds is not null)
            query = query.Where(r => accessibleIds.Contains(r.Id));

        if (projectId.HasValue)
            query = query.Where(r => r.ProjectId == projectId.Value);

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(r => r.Version.Contains(search) || r.BranchName.Contains(search));

        var totalCount = await query.CountAsync(ct).ConfigureAwait(false);

        var items = await query
            .Include(r => r.Project)
            .Include(r => r.Artifacts)
            .AsSplitQuery()
            // Keep unpublished (PublishedAt == null) releases LAST, matching the old `?? MinValue`
            // behavior WITHOUT wrapping every row's PublishedAt in a COALESCE: Postgres DESC orders
            // NULLs FIRST, so a plain `OrderByDescending(PublishedAt)` would surface drafts on top.
            // The leading `PublishedAt != null` key (`ORDER BY (PublishedAt IS NOT NULL) DESC`) pins
            // published releases above drafts. (Postgres still sorts on that boolean expression - the
            // btree index on PublishedAt only helps the tie-break within each null/non-null group, not
            // the leading key; the win here is dropping the per-row COALESCE, not a full index sort.)
            .OrderByProperty(sortBy, sortDescending, ordered => ordered
                .OrderByDescending(r => r.PublishedAt != null)
                .ThenByDescending(r => r.PublishedAt)
                .ThenByDescending(r => r.Id))
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return (items, totalCount);
    }

    public async Task<List<Release>> GetProjectReleasesAsync(int projectId, CancellationToken ct = default)
    {
        return await db.Releases
            .AsNoTracking()
            .Where(r => r.ProjectId == projectId)
            .Include(r => r.Project)
            .Include(r => r.Artifacts)
            .AsSplitQuery()
            // Keep unpublished (null PublishedAt) releases LAST - see GetReleasesPagedAsync.
            .OrderByDescending(r => r.PublishedAt != null)
            .ThenByDescending(r => r.PublishedAt)
            .ThenByDescending(r => r.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<Release?> FindReleaseAsync(int id, CancellationToken ct = default)
    {
        return await db.Releases
            .Include(r => r.Project)
            .Include(r => r.Artifacts)
            .Include(r => r.Commits)
            .Include(r => r.Branches)
            .AsSplitQuery()
            .FirstOrDefaultAsync(r => r.Id == id, ct)
            .ConfigureAwait(false);
    }

    public async Task<Release?> FindByVersionAsync(int projectId, string version, CancellationToken ct = default)
    {
        return await db.Releases
            .FirstOrDefaultAsync(r => r.ProjectId == projectId && r.Version == version, ct)
            .ConfigureAwait(false);
    }

    public async Task<List<Release>> GetDeployedProjectReleasesAsync(int projectId, CancellationToken ct = default) =>
        await db.Releases
            .Where(release => release.ProjectId == projectId && release.Status == ReleaseStatus.Deployed)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public async Task AddReleaseAsync(Release release, CancellationToken ct = default)
    {
        db.Releases.Add(release);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<Release?> FindByPipelineRunIdAsync(int pipelineRunId, CancellationToken ct = default)
    {
        return await db.Releases
            .FirstOrDefaultAsync(r => r.PipelineRunId == pipelineRunId, ct)
            .ConfigureAwait(false);
    }

    public async Task<Release?> FindPreviousPublishedWithArtifactAsync(Release release, CancellationToken ct = default)
    {
        // A release can only be rolled back inside one deployment cohort. Older rows that predate
        // the cohort metadata, or a release spanning several cohorts, are intentionally ineligible:
        // choosing an arbitrary environment would be worse than refusing the request.
        var cohorts = release.Artifacts
            .Select(artifact => artifact.EnvironmentName)
            .Where(environmentName => !string.IsNullOrWhiteSpace(environmentName))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (cohorts.Count != 1) return null;

        var cohort = cohorts[0];
        var publishedAt = release.PublishedAt ?? release.DetectedAt;
        return await db.Releases
            .Where(candidate => candidate.ProjectId == release.ProjectId
                && candidate.Id != release.Id
                && candidate.PublishedAt != null
                && (candidate.Status == ReleaseStatus.Published
                    || candidate.Status == ReleaseStatus.Promoted
                    || candidate.Status == ReleaseStatus.Deployed
                    || candidate.Status == ReleaseStatus.Superseded)
                && (candidate.PublishedAt < publishedAt
                    || (candidate.PublishedAt == publishedAt && candidate.Id < release.Id))
                && candidate.Artifacts.Any(artifact => artifact.EnvironmentName == cohort))
            .Include(candidate => candidate.Project)
            .Include(candidate => candidate.Artifacts.Where(artifact => artifact.EnvironmentName == cohort))
            .OrderByDescending(candidate => candidate.PublishedAt)
            .ThenByDescending(candidate => candidate.Id)
            .AsSplitQuery()
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task AddRollbackAsync(ReleaseRollback rollback, CancellationToken ct = default)
    {
        db.ReleaseRollbacks.Add(rollback);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<ReleaseRollback?> FindRollbackByPipelineRunIdAsync(int pipelineRunId, CancellationToken ct = default)
    {
        return await db.ReleaseRollbacks
            .Include(r => r.SourceRelease).ThenInclude(r => r.Project)
            .Include(r => r.TargetRelease).ThenInclude(r => r.Project)
            .FirstOrDefaultAsync(r => r.PipelineRunId == pipelineRunId, ct)
            .ConfigureAwait(false);
    }

    public async Task<ReleaseRollback?> FindRollbackAsync(int id, CancellationToken ct = default)
    {
        return await db.ReleaseRollbacks
            .Include(r => r.SourceRelease).ThenInclude(r => r.Project)
            .Include(r => r.TargetRelease).ThenInclude(r => r.Project)
            .FirstOrDefaultAsync(r => r.Id == id, ct)
            .ConfigureAwait(false);
    }

    public async Task<List<Release>> GetByPipelineRunIdAsync(int pipelineRunId, CancellationToken ct = default)
    {
        return await db.Releases
            .AsNoTracking()
            .Where(r => r.PipelineRunId == pipelineRunId)
            .Include(r => r.Project)
            .Include(r => r.Artifacts)
            // Keep unpublished (null PublishedAt) releases LAST - see GetReleasesPagedAsync.
            .OrderByDescending(r => r.PublishedAt != null)
            .ThenByDescending(r => r.PublishedAt)
            .ThenByDescending(r => r.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<int> GetMaxBuildNumberAsync(int projectId, CancellationToken ct = default)
    {
        return await db.Releases
            .Where(r => r.ProjectId == projectId)
            .Select(r => (int?)r.BuildNumber)
            .MaxAsync(ct)
            .ConfigureAwait(false) ?? 0;
    }

    /// <summary>
    /// One page of a server's releases, newest first, with the total. A360-18: the unpaged variant
    /// below loads every release of every project the server ever touched, on a table whose whole point
    /// is long retention - fine on a fresh install, an out-of-memory waiting to happen after a year.
    /// </summary>
    public async Task<(List<Release> Items, int TotalCount)> GetReleasesForServerPagedAsync(
        int serverId, int page, int pageSize, CancellationToken ct = default,
        string? sortBy = null, bool sortDescending = true)
    {
        var query = ReleasesForServerQuery(serverId);
        var total = await query.CountAsync(ct).ConfigureAwait(false);
        var items = await query.OrderByProperty(sortBy, sortDescending, OrderForDisplay)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct).ConfigureAwait(false);
        return (items, total);
    }

    /// <summary>
    /// Releases of every project a server is involved in, either by having executed one of its
    /// pipeline steps or by being explicitly linked to it.
    ///
    /// It lived in ServerRepository, which made Servers depend on Pipelines to enrich the result with
    /// the pipeline each release came from. The query is about releases, so it belongs here: Releases
    /// already sits above Pipelines and already performs that enrichment for its own views.
    /// </summary>
    public async Task<List<Release>> GetReleasesForServerAsync(int serverId, CancellationToken ct = default)
    {
        var executedProjectIds = db.PipelineStepRuns
            .Where(step => step.ServerId == serverId)
            .Select(step => step.PipelineRun.Pipeline.ProjectId)
            .Where(projectId => projectId != null)
            .Select(projectId => projectId!.Value);
        var linkedProjectIds = db.ProjectServers
            .Where(projectServer => projectServer.ServerId == serverId)
            .Select(projectServer => projectServer.ProjectId);
        var projectIds = executedProjectIds.Union(linkedProjectIds).Distinct();

        return await OrderForDisplay(db.Releases
                .Where(release => projectIds.Contains(release.ProjectId))
                .Include(release => release.Project)
                .AsNoTracking())
            .ToListAsync(ct).ConfigureAwait(false);
    }

    /// <summary>The projects a server is linked to, or has ever executed a pipeline for.</summary>
    private IQueryable<Release> ReleasesForServerQuery(int serverId)
    {
        var executedProjectIds = db.PipelineStepRuns
            .Where(step => step.ServerId == serverId)
            .Select(step => step.PipelineRun.Pipeline.ProjectId)
            .Where(projectId => projectId != null)
            .Select(projectId => projectId!.Value);
        var linkedProjectIds = db.ProjectServers
            .Where(projectServer => projectServer.ServerId == serverId)
            .Select(projectServer => projectServer.ProjectId);
        var projectIds = executedProjectIds.Union(linkedProjectIds).Distinct();

        return db.Releases
            .Where(release => projectIds.Contains(release.ProjectId))
            .Include(release => release.Project)
            .AsNoTracking();
    }

    /// <summary>
    /// Unpublished (null PublishedAt) releases LAST, without the non-sargable COALESCE - the same NULL
    /// ordering every other release view uses, so draft placement stays consistent.
    /// </summary>
    private static IOrderedQueryable<Release> OrderForDisplay(IQueryable<Release> query)
        => query
            .OrderByDescending(release => release.PublishedAt != null)
            .ThenByDescending(release => release.PublishedAt)
            .ThenByDescending(release => release.Id);

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
