// SPDX-License-Identifier: EUPL-1.2
using System.Text.Json;
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Pipelines;

// Pipeline lifecycle queries: retention, webhook/schedule triggers, active-run checks, stuck-run reconcile.
internal sealed class PipelineLifecycleRepository(AppDbContext db, TimeProvider timeProvider)
{
    public async Task<bool> IsStepRetryEligibleAsync(
        int pipelineRunId,
        int stepRunId,
        CancellationToken ct = default)
    {
        var scannerKey = await db.Set<ServerTask>().AsNoTracking()
            .Where(task => task.PipelineRunId == pipelineRunId
                && task.PipelineStepRunId == stepRunId
                && task.Operation == OperationKind.PipelineRunScanner)
            .OrderByDescending(task => task.Id)
            .Select(task => task.Command)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (scannerKey is null) return true;

        var report = await db.AnalysisReports.AsNoTracking()
            .Where(item => item.PipelineRunId == pipelineRunId && item.ScannerKey == scannerKey)
            .OrderByDescending(item => item.Id)
            .Select(item => new { item.Status, item.ErrorMessage })
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (report is null) return true;
        if (report.Status == AnalysisReportStatus.Unavailable)
            return ContainsTransientMarker(report.ErrorMessage);
        return report.Status == AnalysisReportStatus.Error && ContainsTransientMarker(report.ErrorMessage);
    }

    private static bool ContainsTransientMarker(string? message) =>
        message is not null && new[] { "network", "download", "registry", "temporar", "429", "502", "503", "504" }
            .Any(marker => message.Contains(marker, StringComparison.OrdinalIgnoreCase));

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

    public async Task<int> ResetFailedStepRunsAsync(int runId, CancellationToken ct = default)
    {
        await using var transaction = db.Database.IsRelational() && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false)
            : null;

        // Failure finalization cancels the downstream tail. Reopen it with the failed step so a
        // partial retry cannot finalize green while required stages remain Cancelled.
        var failedSteps = await db.PipelineStepRuns
            .Where(s => s.PipelineRunId == runId
                && (s.Status == TaskExecutionStatus.Failed
                    || s.Status == TaskExecutionStatus.Timeout
                    || s.Status == TaskExecutionStatus.Cancelled))
            .ToListAsync(ct).ConfigureAwait(false);
        if (failedSteps.Count == 0) return 0;

        foreach (var step in failedSteps)
        {
            step.Status = TaskExecutionStatus.Pending;
            step.ExitCode = null;
            step.FailureCode = null;
            step.FailureReason = null;
            step.StartedAt = null;
            step.CompletedAt = null;
        }

        var run = await db.PipelineRuns.FindAsync([runId], ct).ConfigureAwait(false);
        if (run is not null)
        {
            run.Status = PipelineStatus.Running;
            run.CompletedAt = null;
            var variables = PipelineRunHelpers.DeserializeResolvedVariables(run.AdditionalVariablesJson);
            variables.Remove(PipelineRunService.CancellationRequestedVariable);
            run.AdditionalVariablesJson = JsonSerializer.Serialize(variables);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        if (transaction is not null)
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        return failedSteps.Count;
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

    // A trigger step waits on a child run and has NO
    // ServerTask, so TaskTimeoutService never sees it; if the child's completion event was lost (backend
    // restart, missed hook) the step hangs Running forever. The sweeper re-checks these; the age filter
    // keeps it from racing the normal event handler on freshly-dispatched triggers.
    public async Task<List<PipelineStepRun>> GetStuckRunningTriggerStepsAsync(DateTime startedBefore, CancellationToken ct = default)
        => await db.PipelineStepRuns
            .AsNoTracking()
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
                        // Every workspace run owns a deferred cleanup task from the start. While it is
                        // merely Pending it cannot drive normal scheduling, so it must not hide a stalled
                        // run from recovery. Once assigned/running, however, its callback is in flight.
                        && !r.Tasks.Any(t => (t.Status == TaskExecutionStatus.Pending && !t.IsDeferredCleanup)
                                             || t.Status == TaskExecutionStatus.Assigned
                                             || t.Status == TaskExecutionStatus.Running))
            .Select(r => r.Id)
            .ToListAsync(ct).ConfigureAwait(false);

    public async Task<List<int>> GetStalledCancellationRunIdsAsync(DateTime startedBefore, CancellationToken ct = default)
        => await db.PipelineRuns
            .AsNoTracking()
            .Where(r => r.Status == PipelineStatus.Running
                        && r.StartedAt < startedBefore
                        && r.AdditionalVariablesJson.Contains(PipelineRunService.CancellationRequestedVariable)
                        // A pending deferred cleanup is deliberately ignored: reapplying cancellation
                        // releases orphan Running steps, then the scheduler dispatches that cleanup.
                        && !r.Tasks.Any(t => (t.Status == TaskExecutionStatus.Pending && !t.IsDeferredCleanup)
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
            return await db.PipelineRuns
                .Where(r => r.StartedAt < cutoff
                            && !r.Tasks.Any(task => task.IsDeferredCleanup
                                && (task.Status == TaskExecutionStatus.Pending
                                    || task.Status == TaskExecutionStatus.Assigned
                                    || task.Status == TaskExecutionStatus.Running)))
                .ExecuteDeleteAsync(ct).ConfigureAwait(false);

        var old = await db.PipelineRuns
            .Where(r => r.StartedAt < cutoff
                        && !r.Tasks.Any(task => task.IsDeferredCleanup
                            && (task.Status == TaskExecutionStatus.Pending
                                || task.Status == TaskExecutionStatus.Assigned
                                || task.Status == TaskExecutionStatus.Running)))
            .ToListAsync(ct).ConfigureAwait(false);
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

    public async Task<bool> LockPipelineForWebhookAsync(int pipelineId, CancellationToken ct = default)
    {
        if (!db.Database.IsRelational())
            return await db.Pipelines.AnyAsync(pipeline => pipeline.Id == pipelineId, ct).ConfigureAwait(false);

        var rows = await db.Pipelines
            .Where(pipeline => pipeline.Id == pipelineId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(pipeline => pipeline.Name, pipeline => pipeline.Name),
                ct)
            .ConfigureAwait(false);
        return rows == 1;
    }

    public async Task<List<int>> GetActiveRunIdsAsync(int pipelineId, CancellationToken ct = default)
    {
        return await db.PipelineRuns
            .Where(run => run.PipelineId == pipelineId
                          && (run.Status == PipelineStatus.Running
                              || run.Status == PipelineStatus.Pending
                              || run.Status == PipelineStatus.WaitingForApproval))
            .OrderBy(run => run.Id)
            .Select(run => run.Id)
            .ToListAsync(ct).ConfigureAwait(false);
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

        var runs = await query
            .OrderByDescending(run => run.StartedAt)
            .Take(100)
            .Select(PipelineRunHelpers.RunListProjection)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return runs.Select(PipelineRunHelpers.HydrateListWarnings).ToList();
    }

    public async Task<List<PipelineRunDto>> GetRecentRunsAsync(
        List<int>? accessiblePipelineIds = null, int? projectId = null, int? serverId = null,
        CancellationToken ct = default)
    {
        var query = db.PipelineRuns.AsNoTracking().AsQueryable();
        if (accessiblePipelineIds is not null)
            query = query.Where(run => accessiblePipelineIds.Contains(run.PipelineId));
        if (projectId.HasValue)
            query = query.Where(run => run.Pipeline.ProjectId == projectId.Value);
        if (serverId.HasValue)
            query = query.Where(run => run.StepRuns.Any(step => step.ServerId == serverId.Value));

        var runs = await query
            .OrderByDescending(run => run.StartedAt)
            .Take(20)
            .Select(PipelineRunHelpers.RunListProjection)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return runs.Select(PipelineRunHelpers.HydrateListWarnings).ToList();
    }
}
