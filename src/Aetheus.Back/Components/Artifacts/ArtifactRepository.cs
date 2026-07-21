// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.Components.Artifacts;

public sealed class ArtifactRepository(AppDbContext db) : IArtifactRepository
{
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
            .Where(a => a.RetentionExpiresAt <= cutoff)
            .OrderBy(a => a.RetentionExpiresAt)
            .Take(batchSize)
            .ToListAsync(ct).ConfigureAwait(false);

    public async Task<List<PipelineArtifact>> GetProjectBuildArtifactsAsync(
        int projectId, CancellationToken ct = default) =>
        await db.PipelineArtifacts
            .Where(a => a.ProjectId == projectId
                        && a.RetentionPolicy == ArtifactRetentionPolicy.Build
                        // The release link is the authoritative retention boundary. A stale policy
                        // value must never make a rollback payload eligible for quota eviction.
                        && !a.Releases.Any())
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

    public async Task<PipelineArtifact?> FindReleaseArtifactAsync(int projectId, string releaseSelector, CancellationToken ct = default)
        => (await FindReleaseArtifactSelectionAsync(projectId, releaseSelector, ct).ConfigureAwait(false))?.Artifact;

    public async Task<ReleaseArtifactSelection?> FindReleaseArtifactSelectionAsync(
        int projectId, string releaseSelector, CancellationToken ct = default)
    {
        // Resolve the release first (id / version / "latest"), then take its newest linked artifact.
        var releases = db.Releases.Where(r => r.ProjectId == projectId);
        Release? release;
        if (string.Equals(releaseSelector, "latest-published", StringComparison.OrdinalIgnoreCase))
            release = await releases
                .Where(r => r.Status == ReleaseStatus.Published
                            || r.Status == ReleaseStatus.Deployed)
                // Imported/tag-only releases legitimately have no retained payload. "latest-published"
                // means the newest rollback-capable release, not merely the newest metadata row.
                .Where(r => r.Artifacts.Any())
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
            .Where(a => a.Releases.Any(r => r.Id == release.Id))
            .Include(a => a.Project)
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return artifact is null ? null : new ReleaseArtifactSelection(artifact, release.Id);
    }

    public async Task<bool> HasPublishedRollbackContractReleaseAsync(int projectId, CancellationToken ct = default) =>
        await db.Releases
            .AsNoTracking()
            .Where(release => release.ProjectId == projectId
                               && (release.Status == ReleaseStatus.Published
                                   || release.Status == ReleaseStatus.Deployed)
                               // Metadata alone is not a rollback contract: the retained bytes must
                               // still exist and be linked to the release.
                               && release.Artifacts.Any()
                               && release.PipelineRun != null
                              && release.PipelineRun.Pipeline.Name == "aetheus-release-with-rollback")
            .AnyAsync(ct)
            .ConfigureAwait(false);

    public async Task<PipelineArtifact?> FindPreviousPublishedReleaseArtifactAsync(
        int projectId, string currentCommitHash, CancellationToken ct = default)
    {
        var release = await db.Releases
            .Where(candidate => candidate.ProjectId == projectId
                                && (candidate.Status == ReleaseStatus.Published
                                    || candidate.Status == ReleaseStatus.Deployed)
                                && candidate.Artifacts.Any(artifact =>
                                    artifact.PipelineRun.CommitHash != currentCommitHash))
            .OrderByDescending(candidate => candidate.PublishedAt ?? candidate.DetectedAt)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (release is null) return null;

        return await db.PipelineArtifacts
            .Where(artifact => artifact.Releases.Any(candidate => candidate.Id == release.Id)
                               && artifact.PipelineRun.CommitHash != currentCommitHash)
            .Include(artifact => artifact.Project)
            .OrderByDescending(artifact => artifact.CreatedAt)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<PipelineArtifact?> FindPreviousDeployedReleaseArtifactAsync(
        int projectId, string currentCommitHash, CancellationToken ct = default)
    {
        var release = await db.Releases
            .Where(candidate => candidate.ProjectId == projectId
                                && candidate.Status == ReleaseStatus.Deployed
                                && candidate.Artifacts.Any(artifact =>
                                    artifact.PipelineRun.CommitHash != currentCommitHash))
            .OrderByDescending(candidate => candidate.PublishedAt ?? candidate.DetectedAt)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (release is null) return null;

        return await db.PipelineArtifacts
            .Where(artifact => artifact.Releases.Any(candidate => candidate.Id == release.Id)
                               && artifact.PipelineRun.CommitHash != currentCommitHash)
            .Include(artifact => artifact.Project)
            .OrderByDescending(artifact => artifact.CreatedAt)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<bool> HasPreviousPublishedRollbackContractReleaseAsync(
        int projectId, string currentCommitHash, CancellationToken ct = default) =>
        await db.Releases
            .AsNoTracking()
            .Where(release => release.ProjectId == projectId
                              && (release.Status == ReleaseStatus.Published
                                  || release.Status == ReleaseStatus.Deployed)
                              && release.Artifacts.Any(artifact =>
                                  artifact.PipelineRun.CommitHash != currentCommitHash)
                              && release.PipelineRun != null
                              && release.PipelineRun.Pipeline.Name == "aetheus-release-with-rollback")
            .AnyAsync(ct)
            .ConfigureAwait(false);

    public async Task<bool> HasPreviousDeployedRollbackContractReleaseAsync(
        int projectId, string currentCommitHash, CancellationToken ct = default) =>
        await db.Releases
            .AsNoTracking()
            .Where(release => release.ProjectId == projectId
                              && release.Status == ReleaseStatus.Deployed
                              && release.Artifacts.Any(artifact =>
                                  artifact.PipelineRun.CommitHash != currentCommitHash)
                              && release.PipelineRun != null
                              && release.PipelineRun.Pipeline.Name == "aetheus-release-with-rollback")
            .AnyAsync(ct)
            .ConfigureAwait(false);

    public async Task<int?> GetServerOrganizationIdAsync(int serverId, CancellationToken ct = default) =>
        await db.Servers
            .Where(s => s.Id == serverId)
            .Select(s => (int?)s.OrganizationId)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

    public async Task SaveChangesAsync(CancellationToken ct = default) =>
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
}
