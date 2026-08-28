// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Hubs;
using Aetheus.Shared.Helpers;
using Microsoft.AspNetCore.SignalR;
using static Aetheus.Back.Components.Pipelines.PipelineRunHelpers;

namespace Aetheus.Back.Components.Pipelines;

/// <summary>
/// The three operations a human performs on a live run: cancel it, resume it after an approval, retry
/// its failed steps. They share one shape - check the run is in a state that allows the move, make the
/// state transition atomically, then hand control back to the scheduler - which is why they come out
/// together.
/// </summary>
public interface IPipelineRunControlService
{
    /// <summary>Cancels a run and every run it triggered, depth-first through the trigger tree.</summary>
    Task<bool> CancelRunAsync(
        int runId, IPipelineRunScheduler scheduler, IPipelineChildRunLauncher launcher, CancellationToken ct);

    /// <summary>Moves a run out of WaitingForApproval and re-dispatches it.</summary>
    Task<bool> ResumeAfterApprovalAsync(
        int runId, IPipelineRunScheduler scheduler, IPipelineChildRunLauncher launcher, CancellationToken ct);

    /// <summary>Re-opens a terminal run, resets its failed steps and re-dispatches.</summary>
    /// <returns><c>true</c> when something was actually retried.</returns>
    Task<bool> RetryFailedStepsAsync(
        int runId, IPipelineRunScheduler scheduler, IPipelineChildRunLauncher launcher, CancellationToken ct);
}

/// <inheritdoc cref="IPipelineRunControlService"/>
public sealed class PipelineRunControlService(
    IPipelineRepository repo,
    IHubContext<PipelineHub> pipelineHub,
    IPipelineRunDefinitionParser definitions) : IPipelineRunControlService
{
    public async Task<bool> CancelRunAsync(
        int runId, IPipelineRunScheduler scheduler, IPipelineChildRunLauncher launcher, CancellationToken ct)
    {
        var root = await repo.GetPipelineRunWithPipelineAsync(runId, ct).ConfigureAwait(false);
        if (root is null || IsTerminal(root.Status)) return false;
        if (!await CancelSingleRunAsync(root, scheduler, launcher, ct).ConfigureAwait(false)) return false;

        var visited = new HashSet<int> { runId };
        var pendingParents = new Queue<int>();
        pendingParents.Enqueue(runId);
        while (pendingParents.TryDequeue(out var parentRunId))
        {
            var childIds = await repo.GetTriggeredChildRunIdsAsync(parentRunId, ct).ConfigureAwait(false);
            foreach (var childId in childIds)
            {
                if (!visited.Add(childId)) continue;
                var child = await repo.GetPipelineRunWithPipelineAsync(childId, ct).ConfigureAwait(false);
                if (child is not null && !IsTerminal(child.Status))
                    await CancelSingleRunAsync(child, scheduler, launcher, ct).ConfigureAwait(false);
                pendingParents.Enqueue(childId);
            }
        }

        return true;
    }

    private async Task<bool> CancelSingleRunAsync(
        PipelineRun run, IPipelineRunScheduler scheduler, IPipelineChildRunLauncher launcher, CancellationToken ct)
    {
        using var runLock = await RunAdvanceLock.Shared.AcquireAsync(run.Id, ct).ConfigureAwait(false);
        var current = await repo.GetPipelineRunWithPipelineAsync(run.Id, ct).ConfigureAwait(false);
        if (current is null || IsTerminal(current.Status)) return false;
        if (current.Status != PipelineStatus.Running
            && !await repo.TryTransitionPipelineRunStatusAsync(
                    current.Id, current.Status, PipelineStatus.Running, ct).ConfigureAwait(false))
            return false;

        var definition = current.Pipeline is null ? null : definitions.Parse(current);
        var preservedStages = definition is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : YamlParsingHelper.FlattenJobs(definition)
                .Where(stage => string.Equals(
                    stage.Condition?.Trim(), "always()", StringComparison.OrdinalIgnoreCase))
                .Select(stage => stage.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        preservedStages.Add(PipelineSystemStages.Cleanup);

        var cancellationAlreadyRequested = HasCancellationRequest(current.AdditionalVariablesJson);
        await repo.RequestPipelineRunCancellationAsync(current.Id, ct).ConfigureAwait(false);
        await repo.CancelPendingStepRunsExceptStagesAsync(
            current.Id, preservedStages, ct).ConfigureAwait(false);
        if (cancellationAlreadyRequested)
        {
            await repo.CancelOrphanedRunningStepRunsExceptStagesAsync(
                current.Id, preservedStages, ct).ConfigureAwait(false);
        }

        // Cancellation is a durable run state, not a warning. The UI reads the marker above and shows
        // one informational notice while mandatory teardown is settling. Persisting a warning here made
        // every repeated click append the same pseudo-error to WarningsJson.
        if (!await repo.HasAnyRunningStepInRunAsync(current.Id, ct).ConfigureAwait(false))
            await scheduler.AdvanceToNextStageOrCompleteAsync(current.Id, launcher, ct).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> ResumeAfterApprovalAsync(
        int runId, IPipelineRunScheduler scheduler, IPipelineChildRunLauncher launcher, CancellationToken ct)
    {
        var run = await repo.GetPipelineRunWithPipelineAsync(runId, ct).ConfigureAwait(false);
        if (run?.Status != PipelineStatus.WaitingForApproval || run.Pipeline is null) return false; // F-017: align the null-guard with sibling methods

        if (!await repo.TryTransitionPipelineRunStatusAsync(
                run.Id, PipelineStatus.WaitingForApproval, PipelineStatus.Running, ct).ConfigureAwait(false))
            return false;

        var definition = definitions.Parse(run);
        if (definition is not null)
        {
            await scheduler.DispatchNextStageWithScopedSecretsAsync(run, definition, launcher, ct).ConfigureAwait(false);
        }
        return true;
    }

    public async Task<bool> RetryFailedStepsAsync(
        int runId, IPipelineRunScheduler scheduler, IPipelineChildRunLauncher launcher, CancellationToken ct)
    {
        var run = await repo.GetPipelineRunWithPipelineAsync(runId, ct).ConfigureAwait(false);
        if (run?.Pipeline is null
            || run.Status is not (PipelineStatus.Failed or PipelineStatus.Cancelled))
            return false;

        // Repository re-opens the run (Status → Running, CompletedAt → null) and resets the
        // failed step runs plus the downstream tail cancelled during failure finalization to
        // Pending atomically. Zero reset → nothing to retry.
        var reset = await repo.ResetFailedStepRunsAsync(runId, ct).ConfigureAwait(false);
        if (reset == 0) return false;

        var definition = definitions.Parse(run);
        if (definition is not null)
        {
            await scheduler.DispatchNextStageWithScopedSecretsAsync(run, definition, launcher, ct).ConfigureAwait(false);
        }

        await pipelineHub.Clients
            .Groups(HubGroups.PipelineRunUpdates(runId, run.Pipeline.Id))
            .SendAsync("PipelineRunStarted", runId, run.Pipeline.Id, ct).ConfigureAwait(false);

        return true;
    }

    private static bool IsTerminal(PipelineStatus status) =>
        status is PipelineStatus.Success or PipelineStatus.Failed or PipelineStatus.Cancelled;
}
