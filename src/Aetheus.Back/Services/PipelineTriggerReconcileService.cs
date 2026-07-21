// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Pipelines;
using Aetheus.Shared.Enums;
using Microsoft.Extensions.Options;

namespace Aetheus.Back.Services;

/// <summary>
/// Self-heals orchestration runs wedged on a <c>type: trigger</c> step. A trigger step waits on a child
/// pipeline run and carries NO ServerTask, so <see cref="TaskTimeoutService"/> (which only sweeps tasks)
/// never touches it: if the child's completion event is lost - a backend restart between the child
/// finishing and <c>PipelineRunCompletedTriggerHandler</c> firing, a missed hook - the parent step stays
/// Running forever and the whole run hangs (the class of the stuck run #49).
///
/// Each sweep re-reads Running trigger steps older than a short grace and, using the child run's current
/// status, mirrors a terminal child onto the step (exactly like the event handler) then re-enters
/// <c>AdvanceStageAsync</c> so the parent stage can progress. A hard <see cref="BackgroundServicesOptions.TriggerStepStuckTimeout"/>
/// backstop force-fails a step whose child is itself wedged, so no run can hang indefinitely.
/// </summary>
public sealed class PipelineTriggerReconcileService(
    IServiceScopeFactory scopeFactory,
    ILogger<PipelineTriggerReconcileService> logger,
    IOptions<BackgroundServicesOptions> options,
    TimeProvider timeProvider,
    IPostgresLeaderLease? leaderLease = null) : BackgroundService
{
    private readonly TimeSpan _interval = options.Value.TriggerReconcileInterval;
    private readonly TimeSpan _grace = options.Value.TriggerStepGrace;
    private readonly TimeSpan _schedulerGrace = options.Value.SchedulerRecoveryGrace;
    private readonly TimeSpan _stuckTimeout = options.Value.TriggerStepStuckTimeout;
    private readonly TimeSpan _runStuckTimeout = options.Value.RunStuckTimeout;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (leaderLease is null)
            await RunLeaderLoopAsync(stoppingToken).ConfigureAwait(false);
        else
            await leaderLease.RunAsLeaderAsync("aetheus:pipeline-trigger-reconcile", RunLeaderLoopAsync, stoppingToken).ConfigureAwait(false);
    }

    private async Task RunLeaderLoopAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);
        // do..while: sweep immediately on startup (recovers events lost across the restart), then per interval.
        do
        {
            try
            {
                await ReconcileAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is Microsoft.EntityFrameworkCore.DbUpdateException or InvalidOperationException or TimeoutException)
            {
                logger.LogError(ex, "Recoverable error in PipelineTriggerReconcileService");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    internal async Task ReconcileAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IPipelineRepository>();

        var now = timeProvider.GetUtcNow().UtcDateTime;
        await ReconcileTriggerStepsAsync(scope, repo, now, ct).ConfigureAwait(false);
        await ReconcileStalledSchedulingAsync(scope, repo, now, ct).ConfigureAwait(false);
        await FailStuckRunsAsync(scope, repo, now, ct).ConfigureAwait(false);
    }

    // Pass 1: self-heal trigger steps whose child run already reached a terminal state (lost completion
    // event), and backstop-fail those whose child chain is itself wedged past the hard ceiling.
    private async Task ReconcileTriggerStepsAsync(AsyncServiceScope scope, IPipelineRepository repo, DateTime now, CancellationToken ct)
    {
        var steps = await repo.GetStuckRunningTriggerStepsAsync(now - _grace, ct).ConfigureAwait(false);
        if (steps.Count == 0) return;

        var childIds = steps.Select(s => s.TriggeredRunId!.Value).Distinct().ToList();
        var childStatuses = await repo.GetRunStatusesByIdsAsync(childIds, ct).ConfigureAwait(false);

        var toAdvance = new List<(int RunId, string StageName)>();
        foreach (var step in steps)
        {
            var childId = step.TriggeredRunId!.Value;
            var childExists = childStatuses.TryGetValue(childId, out var childStatus);

            TaskExecutionStatus? resolved = null;
            if (childExists && childStatus == PipelineStatus.Success)
            {
                resolved = TaskExecutionStatus.Success;
            }
            else if (childExists && childStatus is PipelineStatus.Failed or PipelineStatus.Cancelled)
            {
                resolved = TaskExecutionStatus.Failed;
            }
            else if (!childExists)
            {
                // Child run row is gone (deleted/retention) but the parent step still waits on it: it can
                // never complete via an event, so fail it rather than hang.
                resolved = TaskExecutionStatus.Failed;
                logger.LogWarning("Trigger step {StepId} (run {ParentRun}) waits on missing child run {ChildRun}; failing it",
                    step.Id, step.PipelineRunId, childId);
            }
            else if (step.StartedAt is { } startedAt && (now - startedAt) > _stuckTimeout)
            {
                // Child still non-terminal past the hard ceiling (its own chain is wedged): backstop-fail.
                resolved = TaskExecutionStatus.Failed;
                logger.LogWarning("Trigger step {StepId} (run {ParentRun}) exceeded {Hours}h waiting on child run {ChildRun} ({ChildStatus}); failing it",
                    step.Id, step.PipelineRunId, _stuckTimeout.TotalHours, childId, childStatus);
            }

            if (resolved is null) continue;

            step.Status = resolved.Value;
            step.ExitCode = resolved == TaskExecutionStatus.Success ? 0 : 1;
            step.CompletedAt = now;
            toAdvance.Add((step.PipelineRunId, step.StageName));

            logger.LogInformation(
                "Reconciled trigger step {StepId} (run {ParentRun}) to {Status} from child run {ChildRun} - completion event was lost",
                step.Id, step.PipelineRunId, resolved.Value, childId);
        }

        if (toAdvance.Count == 0) return;

        await repo.SaveChangesAsync(ct).ConfigureAwait(false);

        var runService = scope.ServiceProvider.GetRequiredService<IPipelineRunService>();
        foreach (var (runId, stageName) in toAdvance)
            await runService.AdvanceStageAsync(runId, stageName, ct).ConfigureAwait(false);
    }

    // Pass 2: a task completion can be persisted while its scheduler callback is lost (restart,
    // cancellation race, transient exception). Such a run has Pending steps but no work in flight and
    // therefore no future callback. Re-drive the stage machine after a short grace instead of waiting
    // forever. The run-level lock makes this safe if a delayed normal callback arrives concurrently.
    private async Task ReconcileStalledSchedulingAsync(
        AsyncServiceScope scope, IPipelineRepository repo, DateTime now, CancellationToken ct)
    {
        var runIds = await repo.GetStalledSchedulableRunIdsAsync(now - _schedulerGrace, ct).ConfigureAwait(false);
        if (runIds.Count == 0) return;

        var runService = scope.ServiceProvider.GetRequiredService<IPipelineRunService>();
        foreach (var runId in runIds)
        {
            logger.LogWarning("Run {RunId} has pending pipeline steps but no in-flight work; re-driving its scheduler", runId);
            await runService.ReconcileRunAsync(runId, ct).ConfigureAwait(false);
        }
    }

    // Pass 3: run-level backstop. A run wedged in Running with no in-flight task and no trigger step
    // waiting on a child can never finalize on its own (e.g. a step failed but the run stayed open, or
    // every task completed without the run closing). Force-fail it so it stops spinning forever.
    private async Task FailStuckRunsAsync(AsyncServiceScope scope, IPipelineRepository repo, DateTime now, CancellationToken ct)
    {
        var stuckRunIds = await repo.GetStuckRunningRunIdsAsync(now - _runStuckTimeout, ct).ConfigureAwait(false);
        if (stuckRunIds.Count == 0) return;

        var runService = scope.ServiceProvider.GetRequiredService<IPipelineRunService>();
        foreach (var runId in stuckRunIds)
        {
            logger.LogWarning("Run {RunId} wedged in Running with no in-flight work past {Hours}h; force-failing it",
                runId, _runStuckTimeout.TotalHours);
            await runService.FailStuckRunAsync(runId, ct).ConfigureAwait(false);
        }
    }
}
