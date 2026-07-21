// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.Components.Pipelines;

// Pipeline lifecycle queries: retention, webhook/schedule triggers, active-run checks, stuck-run reconcile.
internal sealed class PipelineLifecycleRepository(AppDbContext db, TimeProvider timeProvider)
{
    public async Task<bool> TryTransitionPipelineRunStatusAsync(
        int runId, PipelineStatus expectedStatus, PipelineStatus newStatus, CancellationToken ct = default)
    {
        var completedAt = newStatus is PipelineStatus.Success or PipelineStatus.Failed or PipelineStatus.Cancelled
            ? timeProvider.GetUtcNow().UtcDateTime
            : (DateTime?)null;

        if (db.Database.IsRelational())
        {
            var rows = await db.PipelineRuns
                .Where(r => r.Id == runId && r.Status == expectedStatus)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(r => r.Status, newStatus)
                    .SetProperty(r => r.CompletedAt, completedAt), ct)
                .ConfigureAwait(false);
            var tracked = db.ChangeTracker.Entries<PipelineRun>().FirstOrDefault(e => e.Entity.Id == runId);
            if (tracked is not null) tracked.State = EntityState.Detached;
            return rows == 1;
        }

        var run = await db.PipelineRuns
            .FirstOrDefaultAsync(r => r.Id == runId && r.Status == expectedStatus, ct)
            .ConfigureAwait(false);
        if (run is null) return false;
        run.Status = newStatus;
        run.CompletedAt = completedAt;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    public async Task<PipelineApproval?> TryResolveApprovalAsync(
        int approvalId, ApprovalStatus decision, DateTime resolvedAt,
        int? resolvedByUserId, string? comments, CancellationToken ct = default)
    {
        if (db.Database.IsRelational())
        {
            var rows = await db.PipelineApprovals
                .Where(a => a.Id == approvalId && a.Status == ApprovalStatus.Pending)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(a => a.Status, decision)
                    .SetProperty(a => a.ResolvedAt, resolvedAt)
                    .SetProperty(a => a.ResolvedByUserId, resolvedByUserId)
                    .SetProperty(a => a.Comments, comments), ct)
                .ConfigureAwait(false);
            if (rows != 1) return null;
            var tracked = db.ChangeTracker.Entries<PipelineApproval>().FirstOrDefault(e => e.Entity.Id == approvalId);
            if (tracked is not null) tracked.State = EntityState.Detached;
        }
        else
        {
            var approval = await db.PipelineApprovals
                .FirstOrDefaultAsync(a => a.Id == approvalId && a.Status == ApprovalStatus.Pending, ct)
                .ConfigureAwait(false);
            if (approval is null) return null;
            approval.Status = decision;
            approval.ResolvedAt = resolvedAt;
            approval.ResolvedByUserId = resolvedByUserId;
            approval.Comments = comments;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return await db.PipelineApprovals
            .AsNoTracking()
            .Include(a => a.Environment)
            .Include(a => a.ResolvedByUser)
            .FirstAsync(a => a.Id == approvalId, ct)
            .ConfigureAwait(false);
    }

    // Tracked (mutated + saved by the reconcile sweeper). A trigger step waits on a child run and has NO
    // ServerTask, so TaskTimeoutService never sees it; if the child's completion event was lost (backend
    // restart, missed hook) the step hangs Running forever. The sweeper re-checks these; the age filter
    // keeps it from racing the normal event handler on freshly-dispatched triggers.
    public async Task<List<PipelineStepRun>> GetStuckRunningTriggerStepsAsync(DateTime startedBefore, CancellationToken ct = default)
        => await db.PipelineStepRuns
            .Where(s => s.Status == TaskExecutionStatus.Running
                        && s.TriggeredRunId != null
                        && s.StartedAt != null
                        && s.StartedAt < startedBefore)
            .ToListAsync(ct).ConfigureAwait(false);

    public async Task<Dictionary<int, PipelineStatus>> GetRunStatusesByIdsAsync(IReadOnlyCollection<int> runIds, CancellationToken ct = default)
        => await db.PipelineRuns
            .AsNoTracking()
            .Where(r => runIds.Contains(r.Id))
            .Select(r => new { r.Id, r.Status })
            .ToDictionaryAsync(x => x.Id, x => x.Status, ct).ConfigureAwait(false);

    public async Task<List<int>> GetStalledSchedulableRunIdsAsync(DateTime startedBefore, CancellationToken ct = default)
        => await db.PipelineRuns
            .AsNoTracking()
            .Where(r => r.Status == PipelineStatus.Running
                        && r.StartedAt < startedBefore
                        // At least one step still needs scheduling.
                        && r.StepRuns.Any(s => s.Status == TaskExecutionStatus.Pending)
                        // Nothing can produce the next normal completion callback.
                        && !r.StepRuns.Any(s => s.Status == TaskExecutionStatus.Assigned
                                                || s.Status == TaskExecutionStatus.Running)
                        && !r.Tasks.Any(t => t.Status == TaskExecutionStatus.Pending
                                             || t.Status == TaskExecutionStatus.Assigned
                                             || t.Status == TaskExecutionStatus.Running))
            .Select(r => r.Id)
            .ToListAsync(ct).ConfigureAwait(false);

    public async Task<List<int>> GetStuckRunningRunIdsAsync(DateTime startedBefore, CancellationToken ct = default)
        => await db.PipelineRuns
            .AsNoTracking()
            .Where(r => r.Status == PipelineStatus.Running
                        && r.StartedAt < startedBefore
                        // No task still in flight: every task is terminal, so nothing will drive the run.
                        && !r.Tasks.Any(t => t.Status == TaskExecutionStatus.Pending
                                             || t.Status == TaskExecutionStatus.Assigned
                                             || t.Status == TaskExecutionStatus.Running)
                        // Not legitimately waiting on a child pipeline (that case is the trigger-step sweep).
                        && !r.StepRuns.Any(s => s.Status == TaskExecutionStatus.Running && s.TriggeredRunId != null))
            .Select(r => r.Id)
            .ToListAsync(ct).ConfigureAwait(false);

    public async Task<int> DeleteRunsOlderThanAsync(DateTime cutoff, CancellationToken ct = default)
    {
        if (db.Database.IsRelational())
            return await db.PipelineRuns.Where(r => r.StartedAt < cutoff).ExecuteDeleteAsync(ct).ConfigureAwait(false);

        var old = await db.PipelineRuns.Where(r => r.StartedAt < cutoff).ToListAsync(ct).ConfigureAwait(false);
        db.PipelineRuns.RemoveRange(old);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return old.Count;
    }

    public async Task<List<Pipeline>> GetWebhookTriggeredPipelinesWithProjectAsync(CancellationToken ct = default)
    {
        return await db.Pipelines
            .AsNoTracking()
            .Where(p => p.TriggerType == PipelineTriggerType.Webhook)
            .Include(p => p.Project)
                .ThenInclude(pr => pr!.GitConnection)
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<List<Pipeline>> GetWebhookTriggeredPipelinesForProjectAsync(int projectId, CancellationToken ct = default)
    {
        return await db.Pipelines
            .AsNoTracking()
            .Where(p => p.ProjectId == projectId && p.TriggerType == PipelineTriggerType.Webhook)
            .Include(p => p.Project)
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<List<Pipeline>> GetScheduledPipelinesAsync(CancellationToken ct = default)
    {
        return await db.Pipelines
            .AsNoTracking()
            .Where(p => p.TriggerType == PipelineTriggerType.Schedule)
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> HasActiveRunAsync(int pipelineId, CancellationToken ct = default)
    {
        return await db.PipelineRuns
            .AnyAsync(r => r.PipelineId == pipelineId &&
                           (r.Status == PipelineStatus.Running || r.Status == PipelineStatus.Pending || r.Status == PipelineStatus.WaitingForApproval), ct)
            .ConfigureAwait(false);
    }

    public async Task<HashSet<int>> GetPipelineIdsWithActiveRunsAsync(CancellationToken ct = default)
    {
        var ids = await db.PipelineRuns
            .Where(r => r.Status == PipelineStatus.Running || r.Status == PipelineStatus.Pending || r.Status == PipelineStatus.WaitingForApproval)
            .Select(r => r.PipelineId)
            .Distinct()
            .ToListAsync(ct).ConfigureAwait(false);
        return ids.ToHashSet();
    }

    public async Task<List<PipelineRunDto>> GetActiveRunsAsync(
        List<int>? accessiblePipelineIds = null, int? projectId = null, CancellationToken ct = default)
    {
        var query = db.PipelineRuns.AsNoTracking()
            .Where(run => run.Status == PipelineStatus.Running
                || run.Status == PipelineStatus.Pending
                || run.Status == PipelineStatus.WaitingForApproval);
        if (accessiblePipelineIds is not null)
            query = query.Where(run => accessiblePipelineIds.Contains(run.PipelineId));
        if (projectId.HasValue)
            query = query.Where(run => run.Pipeline.ProjectId == projectId.Value);

        return await query
            .OrderByDescending(run => run.StartedAt)
            .Take(100)
            .Select(PipelineRunHelpers.RunListProjection)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<List<PipelineRunDto>> GetRecentRunsAsync(
        List<int>? accessiblePipelineIds = null, int? projectId = null, CancellationToken ct = default)
    {
        var query = db.PipelineRuns.AsNoTracking().AsQueryable();
        if (accessiblePipelineIds is not null)
            query = query.Where(run => accessiblePipelineIds.Contains(run.PipelineId));
        if (projectId.HasValue)
            query = query.Where(run => run.Pipeline.ProjectId == projectId.Value);

        return await query
            .OrderByDescending(run => run.StartedAt)
            .Take(20)
            .Select(PipelineRunHelpers.RunListProjection)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }
}
