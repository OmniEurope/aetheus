// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.GitGraph;

public interface IGitGraphRepository
{
    Task<GitCommit?> FindCommitAsync(int id, CancellationToken ct = default);
    Task<GitBranch?> FindBranchAsync(int id, CancellationToken ct = default);

    // Project-scoped graph (S-FEAT-29): most-recent commits with their cross-links, plus all branches.
    Task<List<GitCommit>> FindProjectCommitsAsync(int projectId, int limit, CancellationToken ct = default);
    Task<List<GitBranch>> FindProjectBranchesAsync(int projectId, CancellationToken ct = default);

    // Recording (get-or-create + link): tracked, with the cross-link collections needed to dedup.
    Task<GitCommit?> FindCommitByShaAsync(int projectId, string sha, CancellationToken ct = default);
    Task<GitBranch?> FindBranchByNameAsync(int projectId, string name, CancellationToken ct = default);
    void TrackCommit(GitCommit commit);
    void TrackBranch(GitBranch branch);
    Task SaveChangesAsync(CancellationToken ct = default);

    // Lightweight read-only resolution (run-detail cross-links): bare entity, no includes.
    Task<GitCommit?> FindCommitByShaReadOnlyAsync(int projectId, string sha, CancellationToken ct = default);
    Task<GitBranch?> FindBranchByNameReadOnlyAsync(int projectId, string name, CancellationToken ct = default);
}
