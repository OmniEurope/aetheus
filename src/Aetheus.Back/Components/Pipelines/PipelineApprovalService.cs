// SPDX-License-Identifier: EUPL-1.2
using System.Security.Claims;
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Hubs;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;

namespace Aetheus.Back.Components.Pipelines;

public class PipelineApprovalService(
    IPipelineRepository repo,
    IPipelineRunService runService,
    IHttpContextAccessor httpContextAccessor,
    IHubContext<PipelineHub> pipelineHub,
    TimeProvider timeProvider) : IPipelineApprovalService
{
    public async Task<List<PipelineApprovalDto>> GetApprovalsAsync(int runId, CancellationToken ct = default)
    {
        var approvals = await repo.GetApprovalsAsync(runId, ct).ConfigureAwait(false);
        return approvals.Select(MapApprovalToDto).ToList();
    }

    public async Task<PipelineApprovalDto?> DecideApprovalAsync(int approvalId, ApprovalDecisionRequest request, CancellationToken ct = default)
    {
        if (request.Decision is not (ApprovalStatus.Approved or ApprovalStatus.Rejected))
            return null;

        // F-43: persist who resolved the approval so it surfaces in the audit trail/DTOs.
        var userIdClaim = httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        var resolvedByUserId = int.TryParse(userIdClaim, out var resolvedById) ? resolvedById : (int?)null;
        var approval = await repo.TryResolveApprovalAsync(
            approvalId, request.Decision, timeProvider.GetUtcNow().UtcDateTime,
            resolvedByUserId, request.Comments, ct).ConfigureAwait(false);
        if (approval is null) return null;

        if (request.Decision == ApprovalStatus.Approved)
        {
            if (!await runService.ResumeAfterApprovalAsync(approval.PipelineRunId, ct).ConfigureAwait(false))
                return null;
            var approvedPipelineId = await repo.GetPipelineIdForRunAsync(approval.PipelineRunId, ct).ConfigureAwait(false);
            var approvedGroups = HubGroups.PipelineRunUpdates(approval.PipelineRunId, approvedPipelineId);
            await pipelineHub.Clients.Groups(approvedGroups).SendAsync("ApprovalResolved", approval.PipelineRunId, approval.StageName, "Approved", ct).ConfigureAwait(false);
        }
        else
        {
            var transitioned = await repo.TryTransitionPipelineRunStatusAsync(
                approval.PipelineRunId, PipelineStatus.WaitingForApproval, PipelineStatus.Failed, ct).ConfigureAwait(false);
            if (!transitioned) return null;
            var rejectedPipelineId = await repo.GetPipelineIdForRunAsync(approval.PipelineRunId, ct).ConfigureAwait(false);
            var rejectedGroups = HubGroups.PipelineRunUpdates(approval.PipelineRunId, rejectedPipelineId);
            await pipelineHub.Clients.Groups(rejectedGroups).SendAsync("ApprovalResolved", approval.PipelineRunId, approval.StageName, "Rejected", ct).ConfigureAwait(false);
            await pipelineHub.Clients.Groups(rejectedGroups).SendAsync("PipelineRunCompleted", approval.PipelineRunId, PipelineStatus.Failed, ct).ConfigureAwait(false);
        }

        return MapApprovalToDto(approval);
    }

    private static PipelineApprovalDto MapApprovalToDto(PipelineApproval a) => new()
    {
        Id = a.Id,
        PipelineRunId = a.PipelineRunId,
        StageName = a.StageName,
        EnvironmentId = a.EnvironmentId,
        EnvironmentName = a.Environment?.Name ?? string.Empty,
        Status = a.Status,
        RequestedAt = a.RequestedAt,
        ResolvedAt = a.ResolvedAt,
        ResolvedByUserId = a.ResolvedByUserId,
        ResolvedByUsername = a.ResolvedByUser?.Username,
        Comments = a.Comments
    };
}
