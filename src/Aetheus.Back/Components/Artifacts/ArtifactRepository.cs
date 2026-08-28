// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Artifacts;

public sealed class ArtifactRepository(AppDbContext db) : IArtifactRepository
{
    private static readonly ReleaseStatus[] PublishedReleaseStatuses =
        [ReleaseStatus.Published, ReleaseStatus.Deployed, ReleaseStatus.Superseded];

    private static readonly ReleaseStatus[] DeployedReleaseStatuses =
        [ReleaseStatus.Deployed, ReleaseStatus.Superseded];

    public async Task<PipelineArtifact?> FindAsync(int id, CancellationToken ct = default) =>
        await db.PipelineArtifacts
            .Where(a => a.Id == id)
            .Include(a => a.Pipeline)
            .Include(a => a.Project)
            .Include(a => a.Releases)
            .Include(a => a.Commits)
            .Include(a => a.Branches)
            .Include(a => a.PipelineRun)
            .AsSplitQuery()
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

    public async Task<(List<PipelineArtifact> Items, int TotalCount)> GetByProjectPagedAsync(
        int projectId, ArtifactRetentionPolicy? policy, int? pipelineId, int page, int pageSize, CancellationToken ct = default)
    {
        var query = db.PipelineArtifacts
            .Where(a => a.ProjectId == projectId)
            .AsNoTracking();

        if (policy.HasValue)
            query = query.Where(a => a.RetentionPolicy == policy.Value);
        if (pipelineId.HasValue)
            query = query.Where(a => a.PipelineId == pipelineId.Value);

        var totalCount = await query.CountAsync(ct).ConfigureAwait(false);
        var items = await query
            .Include(a => a.Pipeline)
            .Include(a => a.Releases)
            .Include(a => a.PipelineRun)
            .AsSplitQuery()
            .OrderByDescending(a => a.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct).ConfigureAwait(false);

        return (items, totalCount);
    }

    public async Task<List<PipelineArtifact>> GetByPipelineAndProjectAsync(
        int pipelineId, int projectId, ArtifactRetentionPolicy policy, CancellationToken ct = default) =>
        await db.PipelineArtifacts
            .Where(a => a.PipelineId == pipelineId && a.ProjectId == projectId && a.RetentionPolicy == policy)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(ct).ConfigureAwait(false);

    public async Task<List<PipelineArtifact>> GetByRunAsync(int pipelineRunId, CancellationToken ct = default) =>
        await db.PipelineArtifacts
            .Where(artifact => artifact.PipelineRunId == pipelineRunId)
            .OrderByDescending(artifact => artifact.CreatedAt)
            .ToListAsync(ct).ConfigureAwait(false);

    public async Task<List<PipelineArtifact>> GetByEnvironmentAsync(
        int pipelineId, int projectId, string environmentName, CancellationToken ct = default) =>
        await db.PipelineArtifacts
            .Where(a => a.PipelineId == pipelineId && a.ProjectId == projectId
                        && a.EnvironmentName == environmentName && a.RetentionPolicy == ArtifactRetentionPolicy.Deployed)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(ct).ConfigureAwait(false);

    public async Task<List<PipelineArtifact>> GetReleasesAsync(
        int pipelineId, int projectId, CancellationToken ct = default) =>
        await db.PipelineArtifacts
            .Where(a => a.PipelineId == pipelineId && a.ProjectId == projectId
                        && a.RetentionPolicy == ArtifactRetentionPolicy.Released)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(ct).ConfigureAwait(false);

    public async Task<List<PipelineArtifact>> GetExpiredAsync(DateTime cutoff, int batchSize, CancellationToken ct = default) =>
        await db.PipelineArtifacts
            .Where(a => a.RetentionExpiresAt <= cutoff
                && (a.RetentionLeaseExpiresAt == null || a.RetentionLeaseExpiresAt <= cutoff)
                // The factually deployed release is the rollback baseline. Its payload must survive
                // ordinary retention even when its original deadline has passed; it becomes eligible
                // again as soon as a later deployment supersedes that release.
                && !a.Releases.Any(release => release.Status == ReleaseStatus.Deployed)
                && !db.DependencyTrackOutboxItems.Any(item =>
                    item.PipelineArtifactId == a.Id && item.CompletedAt == null))
            .OrderBy(a => a.RetentionExpiresAt)
            .Take(batchSize)
            .ToListAsync(ct).ConfigureAwait(false);

    public Task<bool> HasActiveRetentionLeaseAsync(
        int artifactId,
        DateTime at,
        CancellationToken ct = default) =>
        db.PipelineArtifacts
            .AsNoTracking()
            .AnyAsync(
                artifact => artifact.Id == artifactId
                    && artifact.RetentionLeaseExpiresAt != null
                    && artifact.RetentionLeaseExpiresAt > at,
                ct);

    public async Task<List<PipelineArtifact>> GetProjectBuildArtifactsAsync(
        int projectId, CancellationToken ct = default) =>
        await db.PipelineArtifacts
            .Where(a => a.ProjectId == projectId
                        && a.RetentionPolicy == ArtifactRetentionPolicy.Build
                        // The release link is the authoritative retention boundary. A stale policy
                        // value must never make a rollback payload eligible for quota eviction.
                        && !a.Releases.Any()
                        // A durable Dependency-Track delivery must retain its exact immutable SBOM
                        // payload until the worker reaches a terminal state.
                        && !db.DependencyTrackOutboxItems.Any(item =>
                            item.PipelineArtifactId == a.Id && item.CompletedAt == null))
            .OrderBy(a => a.CreatedAt)
            .ThenBy(a => a.Id)
            .ToListAsync(ct).ConfigureAwait(false);

    public async Task AddAsync(PipelineArtifact artifact, CancellationToken ct = default)
    {
        db.PipelineArtifacts.Add(artifact);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task LinkReleaseAsync(PipelineArtifact artifact, int releaseId, CancellationToken ct = default)
    {
        var release = await db.Releases.FindAsync([releaseId], ct).ConfigureAwait(false);
        if (release is null) return;
        await db.Entry(artifact).Collection(a => a.Releases).LoadAsync(ct).ConfigureAwait(false);
        if (artifact.Releases.All(r => r.Id != releaseId))
            artifact.Releases.Add(release);
    }

    public async Task RemoveAsync(PipelineArtifact artifact, CancellationToken ct = default)
    {
        db.PipelineArtifacts.Remove(artifact);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<int> GetNextBuildNumberAsync(int projectId, int pipelineId, CancellationToken ct = default)
    {
        var max = await db.PipelineArtifacts
            .Where(a => a.ProjectId == projectId && a.PipelineId == pipelineId)
            .MaxAsync(a => (int?)a.PipelineRunId, ct).ConfigureAwait(false);
        return (max ?? 0) + 1;
    }

    public async Task<long> GetProjectTotalSizeBytesAsync(int projectId, CancellationToken ct = default) =>
        await db.PipelineArtifacts
            .Where(a => a.ProjectId == projectId)
            .SumAsync(a => a.SizeBytes, ct).ConfigureAwait(false);

    public async Task<(int? DefaultDays, int? LatestDays)> GetProjectRetentionOverridesAsync(int projectId, CancellationToken ct = default)
    {
        var p = await db.Projects
            .AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => new { p.ArtifactRetentionDays, p.ArtifactLatestRetentionDays })
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return (p?.ArtifactRetentionDays, p?.ArtifactLatestRetentionDays);
    }

    public async Task<PipelineArtifact?> FindRunArtifactByNameAsync(int runId, string name, CancellationToken ct = default) =>
        await db.PipelineArtifacts
            .Where(a => a.PipelineRunId == runId && a.Name == name)
            .Include(a => a.Project)
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

    public async Task<PipelineArtifact?> FindSuccessfulPipelineArtifactByCommitAsync(
        int projectId, string pipelineName, string commitHash, string name, CancellationToken ct = default) =>
        await db.PipelineArtifacts
            .Where(a => a.ProjectId == projectId
                        && a.Name == name
                        && a.Pipeline.Name == pipelineName
                        && a.PipelineRun.Status == PipelineStatus.Success
                        && a.PipelineRun.CommitHash == commitHash)
            .Include(a => a.Project)
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

    public async Task<PipelineArtifact?> FindReleaseArtifactAsync(
        int projectId, string releaseSelector, string? artifactName = null, CancellationToken ct = default)
        => (await FindReleaseArtifactSelectionAsync(
            projectId, releaseSelector, artifactName, ct).ConfigureAwait(false))?.Artifact;

    public async Task<ReleaseArtifactSelection?> FindReleaseArtifactSelectionAsync(
        int projectId, string releaseSelector, string? artifactName = null, CancellationToken ct = default)
    {
        // Resolve the release first (id / version / "latest"), then select the requested linked
        // artifact. Without an explicit name, retain the legacy newest-linked-artifact behavior.
        var releases = db.Releases.Where(r => r.ProjectId == projectId);
        Release? release;
        if (string.Equals(releaseSelector, "current-deployed", StringComparison.OrdinalIgnoreCase))
            release = await releases
                .Where(r => r.Status == ReleaseStatus.Deployed)
                .Where(r => r.Artifacts.Any(a => artifactName == null || a.Name == artifactName))
                .SingleOrDefaultAsync(ct).ConfigureAwait(false);
        else if (string.Equals(releaseSelector, "latest-published", StringComparison.OrdinalIgnoreCase))
            release = await releases
                .Where(r => r.Status == ReleaseStatus.Published
                            || r.Status == ReleaseStatus.Deployed
                            || r.Status == ReleaseStatus.Superseded)
                // Imported/tag-only releases legitimately have no retained payload. "latest-published"
                // means the newest rollback-capable release, not merely the newest metadata row.
                .Where(r => r.Artifacts.Any(a => artifactName == null || a.Name == artifactName))
                .OrderByDescending(r => r.PublishedAt ?? r.DetectedAt)
                .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        else if (string.Equals(releaseSelector, "latest", StringComparison.OrdinalIgnoreCase))
            release = await releases.OrderByDescending(r => r.DetectedAt).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        else if (int.TryParse(releaseSelector, out var releaseId))
            release = await releases.FirstOrDefaultAsync(r => r.Id == releaseId, ct).ConfigureAwait(false);
        else
            release = await releases.Where(r => r.Version == releaseSelector)
                .OrderByDescending(r => r.DetectedAt).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (release is null) return null;

        var artifact = await db.PipelineArtifacts
            .Where(a => a.Releases.Any(r => r.Id == release.Id)
                        && (artifactName == null || a.Name == artifactName))
            .Include(a => a.Project)
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return artifact is null ? null : new ReleaseArtifactSelection(artifact, release.Id);
    }

    public async Task<bool> HasDeployedRollbackContractReleaseAsync(
        int projectId, CancellationToken ct = default) =>
        await db.Releases
            .AsNoTracking()
            .Where(release => release.ProjectId == projectId
                              && release.Status == ReleaseStatus.Deployed
                              && release.Artifacts.Any()
                              && release.PipelineRun != null
                              && (release.PipelineRun.Pipeline.Name == "aetheus-candidate"
                                  || release.PipelineRun.Pipeline.Name == "aetheus-release-with-rollback"
                                  || release.PipelineRun.Pipeline.Name == "aetheus-release-fast"))
            .AnyAsync(ct)
            .ConfigureAwait(false);

    public async Task<bool> HasPublishedRollbackContractReleaseAsync(int projectId, CancellationToken ct = default) =>
        await db.Releases
            .AsNoTracking()
            .Where(release => release.ProjectId == projectId
                               && (release.Status == ReleaseStatus.Published
                                   || release.Status == ReleaseStatus.Deployed
                                   || release.Status == ReleaseStatus.Superseded)
                               // Metadata alone is not a rollback contract: the retained bytes must
                               // still exist and be linked to the release.
                               && release.Artifacts.Any()
                               && release.PipelineRun != null
                              && (release.PipelineRun.Pipeline.Name == "aetheus-candidate"
                                  || release.PipelineRun.Pipeline.Name == "aetheus-release-with-rollback"
                                  || release.PipelineRun.Pipeline.Name == "aetheus-release-fast"))
            .AnyAsync(ct)
            .ConfigureAwait(false);

    public async Task<PipelineArtifact?> FindPreviousPublishedReleaseArtifactAsync(
        int projectId, string currentCommitHash, string? artifactName = null, CancellationToken ct = default) =>
        await FindPreviousReleaseArtifactAsync(
            projectId, currentCommitHash, artifactName, PublishedReleaseStatuses, ct).ConfigureAwait(false);

    public async Task<PipelineArtifact?> FindPreviousDeployedReleaseArtifactAsync(
        int projectId, string currentCommitHash, string? artifactName = null, CancellationToken ct = default) =>
        await FindPreviousReleaseArtifactAsync(
            projectId, currentCommitHash, artifactName, DeployedReleaseStatuses, ct).ConfigureAwait(false);

    public async Task<bool> RequiresPreviousPublishedArtifactAsync(
        int projectId, string currentCommitHash, string? artifactName, CancellationToken ct = default) =>
        await RequiresPreviousArtifactAsync(
            projectId, currentCommitHash, artifactName, PublishedReleaseStatuses, ct).ConfigureAwait(false);

    public async Task<bool> RequiresPreviousDeployedArtifactAsync(
        int projectId, string currentCommitHash, string? artifactName, CancellationToken ct = default) =>
        await RequiresPreviousArtifactAsync(
            projectId, currentCommitHash, artifactName, DeployedReleaseStatuses, ct).ConfigureAwait(false);

    private async Task<PipelineArtifact?> FindPreviousReleaseArtifactAsync(
        int projectId,
        string currentCommitHash,
        string? artifactName,
        ReleaseStatus[] statuses,
        CancellationToken ct)
    {
        var release = await db.Releases
            .Where(candidate => candidate.ProjectId == projectId
                                && statuses.Contains(candidate.Status)
                                && candidate.Artifacts.Any(artifact =>
                                    artifact.PipelineRun.CommitHash != currentCommitHash))
            .OrderByDescending(candidate => candidate.PublishedAt ?? candidate.DetectedAt)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (release is null)
            return null;

        return await db.PipelineArtifacts
            .Where(artifact => artifact.Releases.Any(candidate => candidate.Id == release.Id)
                               && artifact.PipelineRun.CommitHash != currentCommitHash
                               && (artifactName == null || artifact.Name == artifactName))
            .Include(artifact => artifact.Project)
            .OrderByDescending(artifact => artifact.CreatedAt)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }

    private async Task<bool> RequiresPreviousArtifactAsync(
        int projectId,
        string currentCommitHash,
        string? artifactName,
        ReleaseStatus[] statuses,
        CancellationToken ct)
    {
        var pipelineName = await db.Releases
            .AsNoTracking()
            .Where(release => release.ProjectId == projectId
                              && statuses.Contains(release.Status)
                              && release.Artifacts.Any(artifact =>
                                    artifact.PipelineRun.CommitHash != currentCommitHash)
                              && release.PipelineRun != null
                              && (release.PipelineRun.Pipeline.Name == "aetheus-candidate"
                                  || release.PipelineRun.Pipeline.Name == "aetheus-release-with-rollback"
                                  || release.PipelineRun.Pipeline.Name == "aetheus-release-fast"))
            .OrderByDescending(release => release.PublishedAt ?? release.DetectedAt)
            .Select(release => release.PipelineRun!.Pipeline.Name)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (pipelineName is null)
            return false;

        return !string.Equals(pipelineName, "aetheus-release-fast", StringComparison.Ordinal)
               || string.Equals(artifactName, "ApplicationPayload-artifacts", StringComparison.Ordinal);
    }

    public async Task<int?> GetServerOrganizationIdAsync(int serverId, CancellationToken ct = default) =>
        await db.Servers
            .Where(s => s.Id == serverId)
            .Select(s => (int?)s.OrganizationId)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

    // --- Own-reads over shared entities -----------------------------------------------------------
    // Four queries this module used to obtain by injecting the Pipelines and Releases services. Those
    // injections put Artifacts inside a module cycle for what is, in every case, a read of an entity
    // both modules already share. Reading them here removes the dependency without moving ownership:
    // the writes still belong to Pipelines and Releases.
    //
    // Each query below is a copy of the canonical one, deliberately: a shared query would reinstate
    // the dependency it removes. If the canonical shape changes, these must follow.

    /// <summary>
    /// Pipeline and project a run belongs to. Canonical shape:
    /// <c>PipelineRunService.GetRunPipelineContextAsync</c>, which reads the same two columns through
    /// a full run DTO this module has no use for.
    /// </summary>
    public async Task<(int PipelineId, int? ProjectId)?> GetRunPipelineContextAsync(
        int runId, CancellationToken ct = default)
    {
        // Projected into an anonymous type, not a ValueTuple: EF Core translates a tuple projection
        // into a PostgreSQL record, which Npgsql refuses to read back ("not supported for fields
        // having DataTypeName 'record'"). That threw on every artifact upload. The tuple is composed
        // here instead, so the public shape is unchanged.
        var context = await db.PipelineRuns.AsNoTracking()
            .Where(run => run.Id == runId)
            .Select(run => new { run.PipelineId, run.Pipeline.ProjectId })
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return context is null ? null : (context.PipelineId, context.ProjectId);
    }

    /// <summary>
    /// Whether a server ran any step of a run - the RBAC question an agent upload has to answer.
    /// Canonical shape: <c>PipelineCoreRepository.IsServerAssignedToRunAsync</c>. The semantics that
    /// matter: ANY step, so a run fanned out over several servers authorizes each of them.
    /// </summary>
    public async Task<bool> IsServerAssignedToRunAsync(int runId, int serverId, CancellationToken ct = default) =>
        await db.PipelineStepRuns.AsNoTracking()
            .AnyAsync(step => step.PipelineRunId == runId && step.ServerId == serverId, ct).ConfigureAwait(false);

    /// <summary>
    /// Release produced by a run, if any. Canonical shape:
    /// <c>ReleaseRepository.FindByPipelineRunIdAsync</c>.
    /// </summary>
    public async Task<Release?> FindReleaseForRunAsync(int pipelineRunId, CancellationToken ct = default) =>
        await db.Releases
            .AsNoTracking()
            .FirstOrDefaultAsync(release => release.PipelineRunId == pipelineRunId, ct).ConfigureAwait(false);

    /// <summary>
    /// Deployed releases of a project, which retention must not delete the artifacts of. Canonical
    /// shape: <c>ReleaseRepository.GetDeployedProjectReleasesAsync</c>.
    /// </summary>
    public async Task<List<Release>> GetDeployedProjectReleasesAsync(int projectId, CancellationToken ct = default) =>
        await db.Releases
            .Where(release => release.ProjectId == projectId && release.Status == ReleaseStatus.Deployed)
            .ToListAsync(ct).ConfigureAwait(false);

    public async Task SaveChangesAsync(CancellationToken ct = default) =>
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
}
