// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.Git;

public interface IGitService
{
    Task<List<GitConnectionDto>> GetConnectionsByProjectAsync(int projectId, CancellationToken ct = default);
    Task<GitConnectionDto?> GetConnectionDetailAsync(int id, CancellationToken ct = default);
    Task<GitConnectionDto> CreateConnectionAsync(CreateGitConnectionRequest request, CancellationToken ct = default);
    Task<GitConnectionDto?> UpdateConnectionAsync(int id, UpdateGitConnectionRequest request, CancellationToken ct = default);
    Task<bool> DeleteConnectionAsync(int id, CancellationToken ct = default);
    Task<PaginatedResult<PullRequestDto>> GetPullRequestsAsync(int gitConnectionId, PullRequestPaginationRequest request, CancellationToken ct = default);
    Task<PullRequestDto?> SyncPullRequestAsync(int gitConnectionId, int externalId, PullRequestDto incoming, CancellationToken ct = default);
    Task<List<BranchPolicyDto>> GetBranchPoliciesAsync(int gitConnectionId, CancellationToken ct = default);
    Task<BranchPolicyDto> CreateBranchPolicyAsync(CreateBranchPolicyRequest request, CancellationToken ct = default);
    Task<BranchPolicyDto?> UpdateBranchPolicyAsync(int id, UpdateBranchPolicyRequest request, CancellationToken ct = default);
    Task<bool> DeleteBranchPolicyAsync(int id, CancellationToken ct = default);
    Task ReportPipelineStatusAsync(PipelineStatusReport report, CancellationToken ct = default);

    /// <summary>F-02: resolve the owning ProjectId for a Git connection (for RBAC).</summary>
    Task<int?> GetProjectIdForConnectionAsync(int connectionId, CancellationToken ct = default);

    /// <summary>F-02: resolve the owning ProjectId for a branch policy (for RBAC).</summary>
    Task<int?> GetProjectIdForBranchPolicyAsync(int branchPolicyId, CancellationToken ct = default);

    /// <summary>F-02: resolve the owning ProjectId for a pipeline run (for status-report RBAC).</summary>
    Task<int?> GetProjectIdForRunAsync(int pipelineRunId, CancellationToken ct = default);
}
