// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Git;

public interface IGitLightRepository
{
    Task<List<GitInternalRepo>> GetByProjectAsync(int projectId, CancellationToken ct = default);

    /// <summary>Repos across projects the caller can read. <paramref name="projectIds"/> null = no
    /// restriction (admin/all); an id list restricts to those projects; an empty list should be
    /// short-circuited by the caller (returns no repos).</summary>
    Task<List<GitInternalRepo>> GetAccessibleAsync(List<int>? projectIds, CancellationToken ct = default);
    Task<(List<GitInternalRepo> Items, int TotalCount)> GetAccessiblePagedAsync(
        List<int>? projectIds, int? projectId, string? search, string? sortBy,
        bool sortDescending, int page, int pageSize, CancellationToken ct = default);
    Task<GitInternalRepo?> FindByIdAsync(int id, CancellationToken ct = default);
    Task<GitInternalRepo?> FindByIdWithProjectAsync(int id, CancellationToken ct = default);
    Task<GitInternalRepo?> FindBySlugAsync(int projectId, string slug, CancellationToken ct = default);
    Task<string?> GetProjectDefaultBranchAsync(int projectId, CancellationToken ct = default);
    Task<bool> ProjectExistsAsync(int projectId, CancellationToken ct = default);
    Task<List<GitInternalRepo>> GetAllAsync(CancellationToken ct = default);
    Task AddAsync(GitInternalRepo entity, CancellationToken ct = default);
    Task RemoveAsync(GitInternalRepo entity, CancellationToken ct = default);
    Task<int> GetNextPrNumberAsync(int gitConnectionId, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);

    // Branch Protection
    Task<List<BranchProtectionRule>> GetBranchProtectionRulesAsync(int repoId, CancellationToken ct = default);
    Task<BranchProtectionRule?> FindBranchProtectionRuleAsync(int id, CancellationToken ct = default);
    Task AddBranchProtectionRuleAsync(BranchProtectionRule rule, CancellationToken ct = default);
    Task RemoveBranchProtectionRuleAsync(BranchProtectionRule rule, CancellationToken ct = default);

    // Own-reads over PipelineRun: obtaining them by injecting IPipelineRepository is what kept Git
    // inside the cycle with Pipelines.
    Task<bool> IsRunActiveAsync(int runId, CancellationToken ct = default);
    Task<int?> GetRunProjectIdAsync(int pipelineRunId, CancellationToken ct = default);
}
