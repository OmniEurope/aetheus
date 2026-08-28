// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.AppBackups;

public interface IBackupPolicyService
{
    /// <summary>Lists a page of policies for accessible projects; <c>null</c> = all (admin/wildcard).</summary>
    Task<PaginatedResult<BackupPolicyDto>> GetPoliciesAsync(
        IReadOnlyCollection<int>? accessibleProjectIds, PaginationRequest request, CancellationToken ct = default);
    Task<BackupPolicyDto> CreateAsync(CreateBackupPolicyRequest request, CancellationToken ct = default);
    Task<BackupPolicyDto?> UpdateAsync(int id, UpdateBackupPolicyRequest request, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);

    /// <summary>Project id owning the policy, for authorization; null if the policy does not exist.</summary>
    Task<int?> GetOwningProjectIdAsync(int id, CancellationToken ct = default);

    Task<PaginatedResult<BackupRunDto>> GetRunsAsync(
        int policyId, PaginationRequest request, CancellationToken ct = default);

    /// <summary>Dispatch a backup now; returns the new run id.</summary>
    Task<int> RunNowAsync(int policyId, CancellationToken ct = default);

    // --- Scheduler + agent callbacks ---
    Task<int> TriggerBackupAsync(BackupPolicy policy, CancellationToken ct = default);
    Task TriggerRestoreCheckAsync(BackupRun run, BackupPolicy policy, CancellationToken ct = default);
    Task<bool> ApplyBackupResultAsync(int runId, int agentServerId, BackupExecuteResultDto result, CancellationToken ct = default);
    Task<bool> ApplyRestoreCheckResultAsync(int runId, int agentServerId, RestoreCheckResultDto result, CancellationToken ct = default);
}
