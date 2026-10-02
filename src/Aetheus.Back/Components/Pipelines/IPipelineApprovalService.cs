// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.Pipelines;

public interface IPipelineApprovalService
{
    Task<List<PipelineApprovalDto>> GetApprovalsAsync(int runId, CancellationToken ct = default);
    Task<List<PendingApprovalDto>> GetPendingApprovalsAsync(List<int>? accessiblePipelineIds, CancellationToken ct = default);
    Task<PipelineApprovalDto?> DecideApprovalAsync(int approvalId, ApprovalDecisionRequest request, CancellationToken ct = default);
}
