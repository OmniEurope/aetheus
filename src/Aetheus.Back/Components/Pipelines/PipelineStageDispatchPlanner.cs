// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Pipelines.Events;
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Hubs;
using Microsoft.AspNetCore.SignalR;
using Aetheus.Back.Services.DomainEvents;
using Aetheus.Shared.Helpers;
using static Aetheus.Back.Components.Pipelines.PipelineRunHelpers;

namespace Aetheus.Back.Components.Pipelines;

/// <summary>
/// Works out which stages of a run are ready and turns that into dispatch: picks the runner, honours
/// affinity and throttling, injects the System stages, and decides what a run with nothing ready means -
/// waiting, blocked, or finished.
/// </summary>
public interface IPipelineStageDispatchPlanner
{
    /// <summary>Dispatches whatever is ready in <paramref name="runId"/>.</summary>
    /// <returns><c>true</c> when a stage was cancelled by an unmet condition, so the caller must
    /// re-evaluate - a cancelled stage can unblock the ones that depended on it.</returns>
    Task<bool> CreateTasksForNextStageAsync(
        int runId, PipelineYamlDefinition definition, Dictionary<string, string> resolvedVars,
        List<PipelineStepRun> pendingSteps, int? organizationId, HashSet<string> secretKeys,
        IPipelineChildRunLauncher launcher, CancellationToken ct);

    /// <summary>First-dispatch overload: reuses variables the caller already resolved.</summary>
    Task<bool> CreateTasksForNextStageAsync(
        int runId, PipelineYamlDefinition definition, Dictionary<string, string> resolvedVars,
        HashSet<string> secretKeys, IPipelineChildRunLauncher launcher, CancellationToken ct);
}

/// <summary>
/// The stage-dispatch planner, split out of the scheduler. The analysis expected the scheduler to be one
/// ~560-line class; this half alone measures ~480, so keeping them together would have produced exactly
/// the oversized file this breakdown exists to remove.
///
/// The division is behavioural, not arithmetic: the scheduler decides WHEN a run moves, this decides
/// WHAT is dispatched and WHERE. It reports; it never finalizes a run except through the finalizer it is
/// given (ADR-041).
/// </summary>
public sealed class PipelineStageDispatchPlanner(
    IPipelineRepository repo,
    IPipelineDispatchServerResolver dispatchServers,
    IPipelineEnvironmentCheckGuard environmentChecks,
    IPipelineRunFinalizer finalizer,
    IPipelineSystemTaskFactory systemTasks,
    IPipelineStepTaskDispatcher stepDispatcher,
    IHubContext<PipelineHub> pipelineHub,
    IDomainEventDispatcher domainEvents,
    ILogger<PipelineStageDispatchPlanner> logger) : IPipelineStageDispatchPlanner
{
    public async Task<bool> CreateTasksForNextStageAsync(
        int runId, PipelineYamlDefinition definition, Dictionary<string, string> resolvedVars,
        HashSet<string> secretKeys, IPipelineChildRunLauncher launcher, CancellationToken ct)
    {
        var pendingSteps = await repo.GetPendingStepRunsAsync(runId, ct).ConfigureAwait(false);
        var pipelineIdForRun = await repo.GetPipelineIdForRunAsync(runId, ct).ConfigureAwait(false);
        var organizationId = pipelineIdForRun is null
            ? null
            : await repo.GetPipelineOrganizationIdAsync(pipelineIdForRun.Value, ct).ConfigureAwait(false);
        return await CreateTasksForNextStageAsync(runId, definition, resolvedVars, pendingSteps, organizationId, secretKeys, launcher, ct).ConfigureAwait(false);
    }

    public async Task<bool> CreateTasksForNextStageAsync(
        int runId, PipelineYamlDefinition definition, Dictionary<string, string> resolvedVars,
        List<PipelineStepRun> pendingSteps, int? organizationId, HashSet<string> secretKeys,
        IPipelineChildRunLauncher launcher, CancellationToken ct)
    {
        // Artifact collection is an execution barrier. A periodic reconcile can arrive after the
        // producer step completed but before its operation task did; dispatching a consumer here
        // races publication and can let failure cleanup erase the shared workspace.
        if (await repo.HasActiveArtifactCollectionAsync(runId, ct).ConfigureAwait(false))
            return false;

        var completedStages = await repo.GetCompletedStageNamesAsync(runId, ct).ConfigureAwait(false);
        var terminalStages = await repo.GetTerminalStageNamesAsync(runId, ct).ConfigureAwait(false);

        var previousStageFailed = await repo.HasAnyFailedStepInRunAsync(runId, ct).ConfigureAwait(false);
        var dependencyTerminalStages = previousStageFailed
            ? terminalStages
            : completedStages;

        var flattenedStages = YamlParsingHelper.FlattenJobs(definition);
        var readyStageNames = FindReadyStages(pendingSteps, definition, completedStages, dependencyTerminalStages);
        var readinessOutcome = await HandleNoReadyStageAsync(
            runId, definition, pendingSteps, completedStages, flattenedStages,
            readyStageNames, previousStageFailed, ct).ConfigureAwait(false);
        if (readinessOutcome.HasValue)
            return readinessOutcome.Value;

        // Output variables are run-scoped and may drive the condition of the next stage. They must
        // therefore be available before conditions are evaluated, not only while creating its tasks.
        await InjectOutputVariablesAsync(runId, resolvedVars, ct).ConfigureAwait(false);
        var activeStageNames = await repo.GetActiveStageNamesAsync(runId, ct).ConfigureAwait(false) ?? [];
        var state = new NextStageDispatchState(
            await repo.GetRunAffinityServerIdAsync(runId, ct).ConfigureAwait(false),
            activeStageNames);
        foreach (var stageName in readyStageNames)
            if (await DispatchReadyStageAsync(
                    runId, stageName, definition, resolvedVars, pendingSteps, organizationId,
                    completedStages, flattenedStages, previousStageFailed, secretKeys, state, launcher, ct)
                .ConfigureAwait(false)) return false;

        return await FinalizeNextStageDispatchAsync(
            runId, state.SystemTaskDispatchFailed, state.AnyTaskCreated, state.AnyStageSyncFailed,
            state.AnyStageCancelled, state.AnyThrottled, state.AnyRunnerTemporarilyUnavailable,
            state.AnyEnvironmentCheckFailed, state.UnmatchedReasons, state.UnmatchedStageSteps, ct)
            .ConfigureAwait(false);
    }

    private async Task<bool> DispatchReadyStageAsync(
        int runId,
        string stageName,
        PipelineYamlDefinition definition,
        Dictionary<string, string> resolvedVars,
        List<PipelineStepRun> pendingSteps,
        int? organizationId,
        List<string> completedStages,
        List<PipelineStageDefinition> flattenedStages,
        bool previousStageFailed,
        HashSet<string> secretKeys,
        NextStageDispatchState state,
        IPipelineChildRunLauncher launcher, CancellationToken ct)
    {
        var stageSteps = pendingSteps.Where(step => step.StageName == stageName).ToList();
        if (await HandleSystemStageAsync(
                runId, stageName, stageSteps, resolvedVars, definition, organizationId,
                completedStages, flattenedStages, state, ct).ConfigureAwait(false)) return false;
        var preparation = await PrepareReadyStageAsync(
            runId, stageName, stageSteps, resolvedVars, completedStages, flattenedStages,
            previousStageFailed, secretKeys, state, ct).ConfigureAwait(false);
        if (preparation.StopDispatch) return true;
        if (preparation.Definition is null) return false;
        var stageDef = preparation.Definition;
        var server = await ResolveDispatchServerAsync(
            runId, stageName, stageDef, stageSteps, resolvedVars, organizationId, state, ct)
            .ConfigureAwait(false);
        if (server is null) return false;
        if (await stepDispatcher.CreateTasksForStageAsync(
                runId, server, stageSteps, stageDef, resolvedVars, completedStages,
                previousStageFailed, secretKeys, organizationId, launcher, ct).ConfigureAwait(false)) return true;
        await RecordStageDispatchAsync(stageSteps, stageDef, state, ct).ConfigureAwait(false);
        return false;
    }

    private async Task<bool> HandleSystemStageAsync(
        int runId,
        string stageName,
        List<PipelineStepRun> stageSteps,
        Dictionary<string, string> resolvedVars,
        PipelineYamlDefinition definition,
        int? organizationId,
        List<string> completedStages,
        List<PipelineStageDefinition> flattenedStages,
        NextStageDispatchState state,
        CancellationToken ct)
    {
        var dispatch = await TryDispatchSystemStageAsync(
            runId, stageName, stageSteps, resolvedVars, definition, organizationId,
            completedStages, flattenedStages, ct).ConfigureAwait(false);
        if (!dispatch.Handled) return false;
        state.AnyTaskCreated |= dispatch.TaskCreated;
        state.AnyRunnerTemporarilyUnavailable |= dispatch.RunnerTemporarilyUnavailable;
        state.SystemTaskDispatchFailed |= dispatch.DispatchFailed;
        state.UnmatchedReasons.AddRange(dispatch.Reasons);
        state.UnmatchedStageSteps.AddRange(dispatch.UnmatchedSteps);
        return true;
    }

    private async Task<ReadyStagePreparation> PrepareReadyStageAsync(
        int runId,
        string stageName,
        List<PipelineStepRun> stageSteps,
        Dictionary<string, string> resolvedVars,
        List<string> completedStages,
        List<PipelineStageDefinition> flattenedStages,
        bool previousStageFailed,
        HashSet<string> secretKeys,
        NextStageDispatchState state,
        CancellationToken ct)
    {
        var stageDef = flattenedStages.FirstOrDefault(stage => stage.Name == stageName);
        if (stageDef is null) return ReadyStagePreparation.Skip;
        if (!EvaluateCondition(stageDef.Condition, resolvedVars, completedStages, previousStageFailed))
        {
            finalizer.MarkStepsConditionNotMet(runId, stageSteps, stageDef.Condition!, resolvedVars, secretKeys);
            await repo.SaveChangesAsync(ct).ConfigureAwait(false);
            state.AnyStageCancelled = true;
            return ReadyStagePreparation.Skip;
        }
        if (IsGroupThrottled(stageDef, flattenedStages, state.ActiveStageNames, state.DispatchedPerGroup))
        {
            state.AnyThrottled = true;
            return ReadyStagePreparation.Skip;
        }
        if (await CheckAndCreateApprovalAsync(runId, stageName, stageDef, ct).ConfigureAwait(false))
            return ReadyStagePreparation.Stop;
        if (await environmentChecks.CheckEnvironmentChecksAsync(stageDef, ct).ConfigureAwait(false) == false)
        {
            finalizer.MarkStepsAs(stageSteps, TaskExecutionStatus.Failed);
            await repo.SaveChangesAsync(ct).ConfigureAwait(false);
            state.AnyEnvironmentCheckFailed = true;
            return ReadyStagePreparation.Skip;
        }
        return ReadyStagePreparation.Prepared(stageDef);
    }

    private async Task<Server?> ResolveDispatchServerAsync(
        int runId,
        string stageName,
        PipelineStageDefinition stageDef,
        List<PipelineStepRun> stageSteps,
        Dictionary<string, string> resolvedVars,
        int? organizationId,
        NextStageDispatchState state,
        CancellationToken ct)
    {
        var stageHasDeploy = dispatchServers.StageHasDeployStep(stageDef);
        var resolution = await ResolveReadyStageServerAsync(
            runId, stageName, stageDef, resolvedVars, organizationId,
            stageHasDeploy, state.AffinityServerId, ct).ConfigureAwait(false);
        if (resolution.Server is null)
        {
            state.AnyRunnerTemporarilyUnavailable |= resolution.RunnerTemporarilyUnavailable;
            state.AddUnmatched(resolution.Reason, stageSteps);
            return null;
        }
        var violation = CheckIsolationPolicy(stageName, stageDef, resolution.Server)
                        ?? dispatchServers.CheckDeploymentPolicy(stageName, stageHasDeploy, resolution.Server);
        if (violation is not null)
        {
            state.AddUnmatched(violation, stageSteps);
            return null;
        }
        if (!stageHasDeploy) state.AffinityServerId ??= resolution.Server.Id;
        return resolution.Server;
    }

    private async Task RecordStageDispatchAsync(
        List<PipelineStepRun> stageSteps,
        PipelineStageDefinition stageDef,
        NextStageDispatchState state,
        CancellationToken ct)
    {
        if (stageSteps.Any(step => step.Status is TaskExecutionStatus.Pending
                or TaskExecutionStatus.Assigned or TaskExecutionStatus.Running))
        {
            state.AnyTaskCreated = true;
            if (!string.IsNullOrEmpty(stageDef.Group))
                state.DispatchedPerGroup[stageDef.Group] =
                    state.DispatchedPerGroup.GetValueOrDefault(stageDef.Group) + 1;
            return;
        }
        if (stageSteps.Any(step => step.Status is TaskExecutionStatus.Failed or TaskExecutionStatus.Timeout))
        {
            state.AnyStageSyncFailed = true;
            return;
        }
        await repo.SaveChangesAsync(ct).ConfigureAwait(false);
        state.AnyStageCancelled = true;
    }

    private sealed class NextStageDispatchState(int? affinityServerId, List<string> activeStageNames)
    {
        public bool AnyTaskCreated { get; set; }
        public bool AnyStageCancelled { get; set; }
        public bool AnyStageSyncFailed { get; set; }
        public bool AnyThrottled { get; set; }
        public bool AnyRunnerTemporarilyUnavailable { get; set; }
        public bool AnyEnvironmentCheckFailed { get; set; }
        public bool SystemTaskDispatchFailed { get; set; }
        public int? AffinityServerId { get; set; } = affinityServerId;
        public List<string> ActiveStageNames { get; } = activeStageNames;
        public Dictionary<string, int> DispatchedPerGroup { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> UnmatchedReasons { get; } = [];
        public List<PipelineStepRun> UnmatchedStageSteps { get; } = [];

        public void AddUnmatched(string? reason, IEnumerable<PipelineStepRun> steps)
        {
            if (reason is null) return;
            UnmatchedReasons.Add(reason);
            UnmatchedStageSteps.AddRange(steps);
        }
    }

    private sealed record ReadyStagePreparation(PipelineStageDefinition? Definition, bool StopDispatch)
    {
        public static ReadyStagePreparation Skip { get; } = new(null, false);
        public static ReadyStagePreparation Stop { get; } = new(null, true);
        public static ReadyStagePreparation Prepared(PipelineStageDefinition definition) => new(definition, false);
    }

    private async Task<bool?> HandleNoReadyStageAsync(
        int runId,
        PipelineYamlDefinition definition,
        List<PipelineStepRun> pendingSteps,
        List<string> completedStages,
        List<PipelineStageDefinition> flattenedStages,
        List<string> readyStageNames,
        bool previousStageFailed,
        CancellationToken ct)
    {
        if (pendingSteps.Count == 0)
        {
            if (await finalizer.IsRunActiveAsync(runId, ct).ConfigureAwait(false))
                await finalizer.FailRunWithUnmatchedStagesAsync(runId,
                    ["Pipeline has no executable steps - every step was skipped or cancelled."],
                    [], ct).ConfigureAwait(false);
            return false;
        }
        if (readyStageNames.Count > 0)
            return null;
        if (await repo.HasAnyRunningStepInRunAsync(runId, ct).ConfigureAwait(false))
            return false;

        if (previousStageFailed)
        {
            var failureHandlerStages = flattenedStages
                .Where(stage => IsFailureHandlerCondition(stage.Condition))
                .Select(stage => stage.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var blockedAfterFailure = pendingSteps
                .Where(step => step.StageName != PipelineRunService.SystemCleanupStage
                    && !failureHandlerStages.Contains(step.StageName))
                .ToList();
            if (blockedAfterFailure.Count > 0)
            {
                finalizer.MarkStepsAs(blockedAfterFailure, TaskExecutionStatus.Cancelled);
                await repo.SaveChangesAsync(ct).ConfigureAwait(false);
                return true;
            }
        }

        // The first pass read completedStages before the running-step check, and the two reads are not
        // atomic: a step finishing between them satisfied both halves of the deadlock test at once - its
        // stage was not complete in the first read, and nothing was running by the second.
        //
        // Re-reading in that same order would only narrow the window, so the order is inverted here.
        // Reading "is anything running" FIRST and the completed stages SECOND makes the conclusion sound
        // whenever a step finishes in between: the later read is the one that observes the completion,
        // so the stage appears ready and no deadlock is declared. A genuine deadlock has nothing running
        // and no newly completed stage, so it still looks identical on this pass and still fails.
        var stillRunning = await repo.HasAnyRunningStepInRunAsync(runId, ct).ConfigureAwait(false);
        var recheckedCompleted = await repo.GetCompletedStageNamesAsync(runId, ct).ConfigureAwait(false);
        var recheckedTerminal = await repo.GetTerminalStageNamesAsync(runId, ct).ConfigureAwait(false);
        var recheckedReady = FindReadyStages(
            pendingSteps, definition, recheckedCompleted,
            previousStageFailed ? recheckedTerminal : recheckedCompleted);
        if (stillRunning || recheckedReady.Count > 0)
            return false;

        if (await finalizer.IsRunActiveAsync(runId, ct).ConfigureAwait(false))
            await finalizer.FailRunWithUnmatchedStagesAsync(runId,
                BuildDeadlockReasons(pendingSteps, definition, recheckedCompleted),
                pendingSteps, ct).ConfigureAwait(false);
        return false;
    }

    private sealed record ReadyStageServerResolution(
        Server? Server,
        bool RunnerTemporarilyUnavailable,
        string? Reason);

    private async Task<ReadyStageServerResolution> ResolveReadyStageServerAsync(
        int runId,
        string stageName,
        PipelineStageDefinition stageDef,
        Dictionary<string, string> resolvedVars,
        int? organizationId,
        bool stageHasDeploy,
        int? affinityServerId,
        CancellationToken ct)
    {
        var effectiveStage = dispatchServers.ResolveEffectiveStageTarget(stageDef, resolvedVars);
        Server? server = null;
        List<int>? configuredTargets = null;
        if (!stageHasDeploy && affinityServerId is not null)
        {
            configuredTargets = await repo.FindCandidateTargetServerIdsAsync(
                effectiveStage.Pool, effectiveStage.Environment, effectiveStage.Agent,
                OsTypeHelper.Parse(effectiveStage.Os), organizationId, false, ct).ConfigureAwait(false);
            if (configuredTargets.Contains(affinityServerId.Value))
            {
                server = await repo.FindOnlineServerByIdAsync(
                    affinityServerId.Value, OsTypeHelper.Parse(effectiveStage.Os), ct).ConfigureAwait(false);
                if (server is null)
                    return new(null, true, null);
            }
        }
        server ??= await dispatchServers.ResolveServerForTargetAsync(
            stageDef, resolvedVars, organizationId, stageHasDeploy, ct).ConfigureAwait(false);
        if (server is not null)
            return new(server, false, null);

        configuredTargets ??= await repo.FindCandidateTargetServerIdsAsync(
            effectiveStage.Pool, effectiveStage.Environment, effectiveStage.Agent,
            OsTypeHelper.Parse(effectiveStage.Os), organizationId, stageHasDeploy, ct).ConfigureAwait(false);
        if (configuredTargets.Count > 0)
        {
            logger.LogWarning(
                "Run {RunId} stage {StageName} is waiting for a configured {TargetKind} to come back online",
                runId, stageName, stageHasDeploy ? "deployment target" : "pipeline runner");
            return new(null, true, null);
        }
        var reason = stageHasDeploy
            ? dispatchServers.BuildNoDeployTargetReason(stageName, stageDef)
            : dispatchServers.BuildNoServerReason(stageName, stageDef);
        return new(null, false, reason);
    }

    private sealed record SystemStageDispatch(
        bool Handled,
        bool TaskCreated,
        bool RunnerTemporarilyUnavailable,
        bool DispatchFailed,
        IReadOnlyList<string> Reasons,
        IReadOnlyList<PipelineStepRun> UnmatchedSteps);

    private async Task<SystemStageDispatch> TryDispatchSystemStageAsync(
        int runId,
        string stageName,
        List<PipelineStepRun> stageSteps,
        Dictionary<string, string> resolvedVars,
        PipelineYamlDefinition definition,
        int? organizationId,
        List<string> completedStages,
        List<PipelineStageDefinition> flattenedStages,
        CancellationToken ct)
    {
        if (stageName is not (PipelineRunService.SystemPrepareStage or PipelineRunService.SystemCleanupStage))
            return new(false, false, false, false, [], []);
        if (await systemTasks.CreateSystemTasksAsync(
                runId, stageSteps, resolvedVars, definition, organizationId, completedStages, ct)
            .ConfigureAwait(false))
            return new(true, true, false, false, [], []);

        var firstStage = flattenedStages.FirstOrDefault();
        var configuredTargets = stageName == PipelineRunService.SystemPrepareStage && firstStage is not null
            ? await repo.FindCandidateTargetServerIdsAsync(
                firstStage.Pool, firstStage.Environment, firstStage.Agent,
                OsTypeHelper.Parse(firstStage.Os), organizationId, false, ct).ConfigureAwait(false)
            : [];
        if (configuredTargets.Count > 0)
        {
            logger.LogWarning(
                "Run {RunId} system stage {StageName} is waiting for a configured runner to come back online",
                runId, stageName);
            return new(true, false, true, false, [], []);
        }
        return new(
            true,
            false,
            false,
            true,
            [$"System stage '{stageName}': no online pipeline runner is available."],
            stageSteps);
    }

    private async Task<bool> FinalizeNextStageDispatchAsync(
        int runId,
        bool systemTaskDispatchFailed,
        bool anyTaskCreated,
        bool anyStageSyncFailed,
        bool anyStageCancelled,
        bool anyThrottled,
        bool anyRunnerTemporarilyUnavailable,
        bool anyEnvCheckFailed,
        List<string> unmatchedReasons,
        List<PipelineStepRun> unmatchedStageSteps,
        CancellationToken ct)
    {
        if (systemTaskDispatchFailed && !await finalizer.IsRunActiveAsync(runId, ct).ConfigureAwait(false))
            return false;
        if (anyTaskCreated)
        {
            await repo.SaveChangesAsync(ct).ConfigureAwait(false);
            return false;
        }
        if (anyStageSyncFailed)
        {
            await repo.SaveChangesAsync(ct).ConfigureAwait(false);
            return true;
        }
        if (anyStageCancelled)
            return true;
        if ((anyThrottled || anyRunnerTemporarilyUnavailable)
            && unmatchedReasons.Count == 0
            && !anyEnvCheckFailed)
            return false;
        await finalizer.FailRunWithUnmatchedStagesAsync(runId, unmatchedReasons, unmatchedStageSteps, ct).ConfigureAwait(false);
        return false;
    }

    // A pipeline needs the workspace (clone/checkout) only if at least one step is NOT a `type: trigger`
    // step. Trigger steps just launch a child run; a pipeline made entirely of them (a pure orchestrator)
    // needs no System:Prepare/Cleanup. Drives both the injection skip and the readiness gate above.
    private static List<string> FindReadyStages(
        List<PipelineStepRun> pendingSteps, PipelineYamlDefinition definition,
        List<string> completedStages, List<string> terminalStages)
    {
        // A trigger-only pipeline injects no System:Prepare (see RequiresWorkspace), so its user stages
        // must not wait on a prepare stage that will never complete - treat prepare as done in that case.
        var prepareCompleted = !PipelineRunPreparationService.RequiresWorkspace(definition) || completedStages.Contains(PipelineRunService.SystemPrepareStage);
        // Flatten once: re-flattening the YAML inside the Where lambda cost a full
        // stages>jobs walk per pending stage on every scheduling pass.
        var flattenedStages = YamlParsingHelper.FlattenJobs(definition);
        // Cleanup must wait for every user stage to reach a terminal state, not for every stage to
        // succeed. A failed stage followed by an always()/failed() teardown is a normal failure path:
        // once the remaining stages are cancelled or settled, the workspace still has to be removed.
        var allUserStagesTerminal = flattenedStages.All(s => terminalStages.Contains(s.Name));

        return pendingSteps
            .Select(s => s.StageName)
            .Distinct()
            .Where(stageName =>
            {
                if (stageName == PipelineRunService.SystemPrepareStage) return true;
                if (stageName == PipelineRunService.SystemCleanupStage) return allUserStagesTerminal;
                if (!prepareCompleted) return false;
                var stageDef = flattenedStages.FirstOrDefault(s => s.Name == stageName);
                if (stageDef is null) return false;
                // A failure handler resolves its dependencies against TERMINAL stages, not completed
                // ones. That is what lets it hang off the last stage it guards and still fire when an
                // earlier one failed: naming a later stage widens what it compensates, naming an
                // earlier one narrows it. The choice belongs to the pipeline, not to the scheduler.
                var satisfiedDependencies = IsFailureHandlerCondition(stageDef.Condition)
                    ? terminalStages
                    : completedStages;
                return stageDef.DependsOn.Count == 0 || stageDef.DependsOn.All(satisfiedDependencies.Contains);
            })
            .ToList();
    }

    // --- P-14: cancellation, approval resume and retry now live in PipelineRunControlService.
    // The engine passes itself as the scheduler: those operations re-enter stage advancement, so the
    // dependency is satisfied per call instead of being injected in both directions.

    private async Task<bool> CheckAndCreateApprovalAsync(
        int runId, string stageName, PipelineStageDefinition stageDef, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(stageDef.Environment)) return false;

        var env = await repo.FindEnvironmentByNameAsync(stageDef.Environment, ct).ConfigureAwait(false);
        if (env is null || !env.RequireApproval) return false;

        // Check if an approval already exists for this run/stage
        var existingApprovals = await repo.GetApprovalsAsync(runId, ct).ConfigureAwait(false);
        if (existingApprovals.Any(a => a.StageName == stageName)) return false;

        var approval = new PipelineApproval
        {
            PipelineRunId = runId,
            StageName = stageName,
            EnvironmentId = env.Id
        };
        await repo.AddApprovalAsync(approval, ct).ConfigureAwait(false);

        if (!await repo.TryTransitionPipelineRunStatusAsync(
                runId, PipelineStatus.Running, PipelineStatus.WaitingForApproval, ct).ConfigureAwait(false))
            return false;
        var approvalPipelineId = await repo.GetPipelineIdForRunAsync(runId, ct).ConfigureAwait(false);
        var approvalGroups = HubGroups.PipelineRunUpdates(runId, approvalPipelineId);
        await pipelineHub.Clients.Groups(approvalGroups).SendAsync("ApprovalRequired", runId, stageName, env.Name, ct).ConfigureAwait(false);
        // Approval observers (audit, notifications) are non-blocking.
        domainEvents.Publish(new PipelineApprovalRequestedEvent(runId, stageName, env.Name));

        return true;
    }

    private async Task InjectOutputVariablesAsync(int runId, Dictionary<string, string> vars, CancellationToken ct)
    {
        var stepOutputs = await repo.GetSuccessfulStepOutputsAsync(runId, ct).ConfigureAwait(false);

        foreach (var step in stepOutputs)
        {
            var outputVars = DeserializeResolvedVariables(step.OutputVariablesJson);
            foreach (var (key, value) in outputVars)
            {
                vars[$"{step.StageName}.{step.StepName}.{key}"] = value;
                if (!key.Equals(PipelineDeploymentTargetGuard.TargetVariable, StringComparison.OrdinalIgnoreCase))
                    vars[key] = value;
            }
        }
    }

    internal static bool IsFailureHandlerCondition(string? condition)
    {
        var normalized = condition?.Trim().ToLowerInvariant();
        return normalized is "always()" or "failed()";
    }
}
