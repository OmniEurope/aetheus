// SPDX-License-Identifier: EUPL-1.2
using System.Security.Claims;
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Hubs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;

namespace Aetheus.Back.Components.Pipelines;

public class PipelineApprovalService(
    IPipelineRepository repo,
    IPipelineRunService runService,
    IHttpContextAccessor httpContextAccessor,
    IHubContext<PipelineHub> pipelineHub,
    ILogger<PipelineApprovalService> logger,
    TimeProvider timeProvider) : IPipelineApprovalService
{
    public async Task<List<PipelineApprovalDto>> GetApprovalsAsync(int runId, CancellationToken ct = default)
    {
        var approvals = await repo.GetApprovalsAsync(runId, ct).ConfigureAwait(false);
        return approvals.Select(MapApprovalToDto).ToList();
    }

    public Task<List<PendingApprovalDto>> GetPendingApprovalsAsync(
        List<int>? accessiblePipelineIds, CancellationToken ct = default)
        => repo.GetPendingApprovalsAsync(accessiblePipelineIds, ct);

    public async Task<PipelineApprovalDto?> DecideApprovalAsync(int approvalId, ApprovalDecisionRequest request, CancellationToken ct = default)
    {
        // F2: TimedOut is a system-issued decision (see PipelineTriggerReconcileService.ExpireStaleApprovalsAsync), never a
        // human choice offered by the UI, but it resolves the run exactly like a Rejected decision -
        // it fails the run and unblocks whoever is waiting on it.
        if (request.Decision is not (ApprovalStatus.Approved or ApprovalStatus.Rejected or ApprovalStatus.TimedOut))
            return null;

        // PLAN-005 lot 3 / D34: a decision on a run that has already ended is refused out loud. Approving
        // would try to resume a finished run; rejecting would claim a failure the run did not have.
        // The system's own TimedOut sweep only ever reaches pending approvals, which an ended run no
        // longer has once CloseApprovalsOfEndedRunsAsync has run.
        var pending = await repo.FindApprovalAsync(approvalId, ct).ConfigureAwait(false);
        if (pending is not null
            && (await repo.GetRunStatusesByIdsAsync([pending.PipelineRunId], ct).ConfigureAwait(false))
                .TryGetValue(pending.PipelineRunId, out var runStatus)
            && runStatus.IsTerminal())
            throw new ConflictException(
                $"Run {pending.PipelineRunId} has already ended ({runStatus}); its approvals can no longer be decided.");

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
            {
                // The approval is already resolved at this point, so returning here leaves the run in
                // WaitingForApproval with nothing pending on it: no decision offered, and invisible to the
                // timeout sweep. Said out loud so it is not silent, and picked up by
                // PipelineTriggerReconcileService.ResolveStrandedApprovalsAsync on its next pass.
                logger.LogError(
                    "Approval {ApprovalId} was approved but run {RunId} did not resume; the run is stranded until the reconciler re-applies it",
                    approvalId, approval.PipelineRunId);
                return null;
            }
            var approvedPipelineId = await repo.GetPipelineIdForRunAsync(approval.PipelineRunId, ct).ConfigureAwait(false);
            var approvedGroups = HubGroups.PipelineRunUpdates(approval.PipelineRunId, approvedPipelineId);
            await pipelineHub.Clients.Groups(approvedGroups).SendAsync("ApprovalResolved", approval.PipelineRunId, approval.StageName, "Approved", ct).ConfigureAwait(false);
        }
        else
        {
            // PLAN-003 2.7: a stage's own confirmation fails that stage, so its Rollback still runs and
            // the run completes later, from its handlers.
            var outcome = await runService.ApplyRefusalAsync(approval.PipelineRunId, ct).ConfigureAwait(false);
            if (outcome is null)
            {
                // Same stranding as the approved branch above, same reconciler picks it up.
                logger.LogError(
                    "Approval {ApprovalId} was {Decision} but run {RunId} did not fail; the run is stranded until the reconciler re-applies it",
                    approvalId, request.Decision, approval.PipelineRunId);
                return null;
            }
            var rejectedPipelineId = await repo.GetPipelineIdForRunAsync(approval.PipelineRunId, ct).ConfigureAwait(false);
            var rejectedGroups = HubGroups.PipelineRunUpdates(approval.PipelineRunId, rejectedPipelineId);
            var reason = request.Decision == ApprovalStatus.TimedOut ? "TimedOut" : "Rejected";
            await pipelineHub.Clients.Groups(rejectedGroups).SendAsync("ApprovalResolved", approval.PipelineRunId, approval.StageName, reason, ct).ConfigureAwait(false);
            if (outcome == PipelineStatus.Failed)
                await pipelineHub.Clients.Groups(rejectedGroups).SendAsync("PipelineRunCompleted", approval.PipelineRunId, PipelineStatus.Failed, ct).ConfigureAwait(false);
        }

        return MapApprovalToDto(approval);
    }

    private static PipelineApprovalDto MapApprovalToDto(PipelineApproval a) => new()
    {
        Id = a.Id,
        PipelineRunId = a.PipelineRunId,
        StageName = a.StageName,
        Scope = a.Scope,
        EnvironmentId = a.EnvironmentId,
        EnvironmentName = a.Environment?.Name,
        Status = a.Status,
        RequestedAt = a.RequestedAt,
        ResolvedAt = a.ResolvedAt,
        ResolvedByUserId = a.ResolvedByUserId,
        ResolvedByUsername = a.ResolvedByUser?.Username,
        Comments = a.Comments
    };
}
