// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Git;

public interface IGitRepository
{
    Task<List<GitConnection>> GetConnectionsByProjectAsync(int projectId, CancellationToken ct = default);
    Task<GitConnection?> GetConnectionDetailAsync(int id, CancellationToken ct = default);
    Task<GitConnection?> FindConnectionAsync(int id, CancellationToken ct = default);
    Task AddConnectionAsync(GitConnection connection, CancellationToken ct = default);
    Task RemoveConnectionAsync(GitConnection connection, CancellationToken ct = default);
    Task<(List<PullRequest> Items, int TotalCount)> GetPullRequestsPagedAsync(
        int gitConnectionId, string? search, int page, int pageSize,
        Aetheus.Shared.Enums.PullRequestStatus? status = null, CancellationToken ct = default);
    Task<PullRequest?> FindPullRequestByExternalIdAsync(int gitConnectionId, int externalId, CancellationToken ct = default);
    Task AddPullRequestAsync(PullRequest pr, CancellationToken ct = default);
    Task<List<BranchPolicy>> GetBranchPoliciesAsync(int gitConnectionId, CancellationToken ct = default);
    Task<BranchPolicy?> FindBranchPolicyAsync(int id, CancellationToken ct = default);
    Task AddBranchPolicyAsync(BranchPolicy policy, CancellationToken ct = default);
    Task RemoveBranchPolicyAsync(BranchPolicy policy, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
