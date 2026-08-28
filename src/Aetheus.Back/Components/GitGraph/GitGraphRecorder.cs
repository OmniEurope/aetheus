// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.GitGraph;

public sealed class GitGraphRecorder(
    IGitGraphRepository repo,
    TimeProvider timeProvider,
    ILogger<GitGraphRecorder> logger) : IGitGraphRecorder
{
    public Task RecordRunContextAsync(int projectId, string? sha, string? branchName, CancellationToken ct = default)
        => RecordAsync(projectId, sha, branchName, release: null, ct);

    public Task RecordReleaseContextAsync(Release release, CancellationToken ct = default)
        => RecordAsync(release.ProjectId, release.CommitHash, release.BranchName, release, ct);

    public async Task<(List<CommitLinkDto> Commits, List<BranchLinkDto> Branches)> ResolveRunLinksAsync(
        int projectId, string? sha, string? branchName, CancellationToken ct = default)
    {
        var commits = new List<CommitLinkDto>();
        var branches = new List<BranchLinkDto>();

        if (!string.IsNullOrWhiteSpace(sha))
        {
            var commit = await repo.FindCommitByShaReadOnlyAsync(projectId, sha, ct).ConfigureAwait(false);
            if (commit is not null)
                commits.Add(new CommitLinkDto { Id = commit.Id, Sha = commit.Sha, Message = commit.Message });
        }

        if (!string.IsNullOrWhiteSpace(branchName))
        {
            var branch = await repo.FindBranchByNameReadOnlyAsync(projectId, branchName, ct).ConfigureAwait(false);
            if (branch is not null)
                branches.Add(new BranchLinkDto { Id = branch.Id, Name = branch.Name });
        }

        return (commits, branches);
    }

    private async Task RecordAsync(int projectId, string? sha, string? branchName, Release? release, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sha) && string.IsNullOrWhiteSpace(branchName)) return;

        // Best-effort provenance: a unique-index race or transient failure must never break the
        // run-trigger / release-create primary flow. Absence of a graph node is not an error.
        try
        {
            var now = timeProvider.GetUtcNow().UtcDateTime;

            var commit = await GetOrCreateCommitAsync(projectId, sha, now, ct).ConfigureAwait(false);
            var branch = await GetOrCreateBranchAsync(projectId, branchName, now, ct).ConfigureAwait(false);

            // Link commit <-> branch (Contains is reference-based on the tracked instances).
            if (commit is not null && branch is not null && !commit.Branches.Contains(branch))
                commit.Branches.Add(branch);

            LinkRelease(commit, branch, release);

            await repo.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "GitGraph recording failed for project {ProjectId} (sha={Sha}, branch={Branch})",
                projectId, sha, branchName);
        }
    }

    private async Task<GitCommit?> GetOrCreateCommitAsync(int projectId, string? sha, DateTime now, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sha)) return null;

        var commit = await repo.FindCommitByShaAsync(projectId, sha, ct).ConfigureAwait(false);
        if (commit is null)
        {
            commit = new GitCommit { ProjectId = projectId, Sha = sha, CreatedAt = now };
            repo.TrackCommit(commit);
        }
        return commit;
    }

    private async Task<GitBranch?> GetOrCreateBranchAsync(int projectId, string? branchName, DateTime now, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(branchName)) return null;

        var branch = await repo.FindBranchByNameAsync(projectId, branchName, ct).ConfigureAwait(false);
        if (branch is null)
        {
            branch = new GitBranch { ProjectId = projectId, Name = branchName, CreatedAt = now };
            repo.TrackBranch(branch);
        }
        return branch;
    }

    // Dedup via the loaded commit/branch.Releases collections (EF fixup keeps the release's own
    // Commits/Branches navigations in sync for the tracked instances).
    private static void LinkRelease(GitCommit? commit, GitBranch? branch, Release? release)
    {
        if (release is null) return;
        if (commit is not null && !commit.Releases.Contains(release))
            commit.Releases.Add(release);
        if (branch is not null && !branch.Releases.Contains(release))
            branch.Releases.Add(release);
    }
}
