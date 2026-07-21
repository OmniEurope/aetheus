// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.DTOs;

namespace Aetheus.Back.Components.Pipelines;

public interface IPipelineApprovalService
{
    Task<List<PipelineApprovalDto>> GetApprovalsAsync(int runId, CancellationToken ct = default);
    Task<PipelineApprovalDto?> DecideApprovalAsync(int approvalId, ApprovalDecisionRequest request, CancellationToken ct = default);
}
