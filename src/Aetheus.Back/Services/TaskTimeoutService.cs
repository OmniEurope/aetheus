// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Pipelines;
using Aetheus.Back.Components.Tasks;
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Hubs;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace Aetheus.Back.Services;

public sealed class TaskTimeoutService(
    IServiceScopeFactory scopeFactory,
    ILogger<TaskTimeoutService> logger,
    IOptions<BackgroundServicesOptions> options,
    IHubContext<ServerHub> serverHub,
    TimeProvider timeProvider) : BackgroundService
{
    private readonly TimeSpan _checkInterval = options.Value.TaskCheckInterval;
    private readonly TimeSpan _taskTimeout = options.Value.TaskTimeout;
    private readonly TimeSpan _assignedStartTimeout = options.Value.AssignedStartTimeout;
    // S-TECH-PTMO: a never-claimed Pending task ages out faster than a claimed/running one.
    private readonly TimeSpan _pendingTimeout = options.Value.PendingTimeout;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_checkInterval);
        // do..while: check immediately on startup, then every interval
        do
        {
            try
            {
                await CheckStaleTasksAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is Microsoft.EntityFrameworkCore.DbUpdateException or InvalidOperationException or TimeoutException)
            {
                logger.LogError(ex, "Recoverable error in TaskTimeoutService");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    internal async Task CheckStaleTasksAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var taskRepo = scope.ServiceProvider.GetRequiredService<ITaskRepository>();

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var timedOutTasks = new List<ServerTask>();

        // Force-fail a task left in any of the three in-flight states past the timeout: Running that
        // started too long ago, Assigned that an agent claimed but never started, and Pending that no
        // agent ever claimed (offline/crashed before polling). The last case is what left the apache2
        // install stuck forever, the older sweeps never looked at Pending.
        var staleRunning = await taskRepo.GetStaleRunningTasksAsync(_taskTimeout, ct).ConfigureAwait(false);
        MarkTimedOut(staleRunning, "Running", _taskTimeout, now, timedOutTasks);

        var staleAssigned = await taskRepo.GetStaleAssignedTasksAsync(_assignedStartTimeout, ct).ConfigureAwait(false);
        MarkTimedOut(staleAssigned, "Assigned", _assignedStartTimeout, now, timedOutTasks);

        // PTMO: Pending uses the shorter dedicated timeout (an unclaimed task signals a dead agent).
        var stalePending = await taskRepo.GetStalePendingTasksAsync(_pendingTimeout, ct).ConfigureAwait(false);
        MarkTimedOut(stalePending, "Pending", _pendingTimeout, now, timedOutTasks);

        if (timedOutTasks.Count > 0)
        {
            await taskRepo.SaveChangesAsync(ct).ConfigureAwait(false);

            // Push TaskCompleted so the top-bar tracker drops the chip live, instead of leaving a dead
            // task spinning until the next reconnect/reseed.
            await BroadcastTimedOutAsync(timedOutTasks, ct).ConfigureAwait(false);
        }

        // Advance pipeline for any timed-out tasks linked to a pipeline run
        var pipelineTasks = timedOutTasks
            .Where(t => t.PipelineRunId.HasValue && t.PipelineStepRunId.HasValue)
            .ToList();

        if (pipelineTasks.Count > 0)
        {
            var pipelineRunService = scope.ServiceProvider.GetRequiredService<IPipelineRunService>();

            // Pre-load all pipeline step runs in a single query instead of one per task.
            var stepRunIds = pipelineTasks.Select(t => t.PipelineStepRunId!.Value).Distinct().ToList();
            var stepRunMap = await taskRepo.FindPipelineStepRunsByIdsAsync(stepRunIds, ct).ConfigureAwait(false);

            var advanceTasks = new List<(int RunId, string StageName)>();
            foreach (var task in pipelineTasks)
            {
                if (stepRunMap.TryGetValue(task.PipelineStepRunId!.Value, out var stepRun)
                    && stepRun.Status is not (TaskExecutionStatus.Success or TaskExecutionStatus.Failed or TaskExecutionStatus.Timeout))
                {
                    stepRun.Status = TaskExecutionStatus.Timeout;
                    stepRun.CompletedAt = now;
                    advanceTasks.Add((task.PipelineRunId!.Value, stepRun.StageName));

                    logger.LogInformation("Advancing pipeline run #{RunId} after task #{TaskId} timeout (stage: {Stage})",
                        task.PipelineRunId, task.Id, stepRun.StageName);
                }
            }

            // Batch-save all step run status changes in one round-trip.
            if (advanceTasks.Count > 0)
                await taskRepo.SaveChangesAsync(ct).ConfigureAwait(false);

            foreach (var (runId, stageName) in advanceTasks)
                await pipelineRunService.AdvanceStageAsync(runId, stageName, ct).ConfigureAwait(false);
        }

        // S-TECH-ARCR: a post-stage artifact-collection task carries a run id but NO step run, so it is
        // excluded from the step-advance loop above. Since the run now WAITS for it before advancing, a
        // collection task that times out (its agent died mid-collect) would otherwise hang the run
        // forever. Fail those runs closed: a CI without its declared artifact is not successful.
        var stuckArtifactRuns = timedOutTasks
            .Where(t => t.PipelineRunId.HasValue && t.PipelineStepRunId is null
                        && t.Operation == OperationKind.PipelineCollectArtifacts)
            .Select(t => t.PipelineRunId!.Value)
            .Distinct()
            .ToList();

        if (stuckArtifactRuns.Count > 0)
        {
            var pipelineRunService = scope.ServiceProvider.GetRequiredService<IPipelineRunService>();
            foreach (var runId in stuckArtifactRuns)
            {
                logger.LogWarning("Failing pipeline run #{RunId} after its artifact-collection task timed out", runId);
                await pipelineRunService.ContinueAfterArtifactCollectionAsync(
                    runId, TaskExecutionStatus.Timeout, ct).ConfigureAwait(false);
            }
        }
    }

    private void MarkTimedOut(List<ServerTask> staleTasks, string state, TimeSpan timeout, DateTime now, List<ServerTask> accumulator)
    {
        foreach (var task in staleTasks)
        {
            task.Status = TaskExecutionStatus.Timeout;
            task.CompletedAt = now;
            task.EnvironmentVariables = TaskEnvProtection.EmptyEnv;
            logger.LogWarning("Task #{TaskId} ({TaskName}) timed out in {State} state after {Minutes} minutes",
                task.Id, task.Name, state, timeout.TotalMinutes);
            accumulator.Add(task);
        }
    }

    private async Task BroadcastTimedOutAsync(List<ServerTask> timedOutTasks, CancellationToken ct)
    {
        foreach (var task in timedOutTasks)
        {
            var notification = new TaskCompletedNotification
            {
                TaskId = task.Id,
                ServerId = task.ServerId,
                TaskName = task.Name,
                Status = TaskExecutionStatus.Timeout,
                ExitCode = task.ExitCode,
                Output = null
            };
            await Task.WhenAll(
                serverHub.Clients.Group(HubGroups.Server(task.ServerId)).SendAsync("TaskCompleted", notification, ct),
                serverHub.Clients.Group(HubGroups.AllServers).SendAsync("TaskCompleted", notification, ct)
            ).ConfigureAwait(false);
        }
    }
}
