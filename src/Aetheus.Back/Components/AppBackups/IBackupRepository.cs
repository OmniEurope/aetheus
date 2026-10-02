// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.AppBackups;

public interface IBackupRepository
{
    void AddPolicy(BackupPolicy policy);
    void DeletePolicy(BackupPolicy policy);
    /// <summary>Paginated policies for the given projects, or all policies when <paramref name="projectIds"/> is null.</summary>
    Task<(List<BackupPolicy> Items, int TotalCount)> GetPoliciesPagedAsync(
        IReadOnlyCollection<int>? projectIds, string? search, string? sortBy,
        bool sortDescending, int page, int pageSize, CancellationToken ct = default,
        IReadOnlyList<GridFilter>? columnFilters = null);
    Task<BackupPolicy?> FindPolicyAsync(int id, CancellationToken ct = default);
    Task<int?> GetProjectOrganizationIdAsync(int projectId, CancellationToken ct = default);
    Task<List<BackupPolicy>> GetEnabledPoliciesAsync(CancellationToken ct = default);

    void AddRun(BackupRun run);
    Task<BackupRun?> FindRunAsync(int id, CancellationToken ct = default);

    /// <summary>Loads a restore candidate with its policy. The caller must still check project, server,
    /// successful dump and verified restore-check before scheduling a destructive restore.</summary>
    Task<BackupRun?> FindRunWithPolicyAsync(int id, CancellationToken ct = default);
    Task<(List<BackupRun> Items, int TotalCount)> GetRunsForPolicyPagedAsync(
        int policyId, string? search, string? sortBy, bool sortDescending,
        int page, int pageSize, CancellationToken ct = default,
        IReadOnlyList<GridFilter>? columnFilters = null);

    /// <summary>Most recent successful run of a policy (candidate for a restore-check), or null.</summary>
    Task<BackupRun?> GetLatestSuccessfulRunAsync(int policyId, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
