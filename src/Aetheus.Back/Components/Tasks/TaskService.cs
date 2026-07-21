// SPDX-License-Identifier: EUPL-1.2
using System.Text.Json;
using Aetheus.Back.Components.Artifacts;
using Aetheus.Back.Components.Audit;
using Aetheus.Back.Components.Logs;
using Aetheus.Back.Components.Pipelines;
using Aetheus.Back.Components.Releases;
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Exceptions;
using Aetheus.Back.Hubs;
using Aetheus.Back.Services;
using Aetheus.Back.Services.DomainEvents;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.SignalR;

namespace Aetheus.Back.Components.Tasks;

public class TaskService(ITaskRepository repo, IPipelineRunService pipelineRunService, ILogService logService, IHubContext<PipelineHub> pipelineHub, IHubContext<ServerHub> serverHub, IAuditService audit, TimeProvider timeProvider, IEncryptionService encryption, IArtifactService artifactService, ILogger<TaskService> logger, IDomainEventDispatcher? domainEvents = null) : ITaskService
{
    public Task<PaginatedResult<ServerTaskDto>> GetTasksAsync(TaskPaginationRequest request, CancellationToken ct = default)
        => GetTasksAsync(request, null, ct);

    public async Task<PaginatedResult<ServerTaskDto>> GetTasksAsync(TaskPaginationRequest request, List<int>? accessibleServerIds = null, CancellationToken ct = default)
    {
        var (page, pageSize) = request.Normalize();
        var (items, totalCount) = await repo.GetTasksPagedAsync(
            request.Search, page, pageSize, request.Status, accessibleServerIds, request.ServerId, ct).ConfigureAwait(false);

        return new PaginatedResult<ServerTaskDto>
        {
            Items = items.Select(MapToDto).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<ServerTaskDto?> GetTaskAsync(int id, CancellationToken ct = default)
    {
        var task = await repo.GetTaskWithServerAsync(id, ct).ConfigureAwait(false);
        return task is null ? null : MapToDto(task);
    }

    public async Task<List<ServerTaskDto>> GetActiveTasksAsync(List<int>? accessibleServerIds, CancellationToken ct = default)
    {
        // Single query for all active statuses instead of three sequential queries.
        // The widget can show ~50 lines; the cap of 200 leaves room for a SaaS-scale
        // fan-out while keeping the JSON payload bounded.
        var activeStatuses = new List<TaskExecutionStatus>
        {
            TaskExecutionStatus.Running,
            TaskExecutionStatus.Assigned,
            TaskExecutionStatus.Pending
        };
        var (items, _) = await repo.GetTasksByStatusesPagedAsync(null, 1, 200, activeStatuses, accessibleServerIds, ct).ConfigureAwait(false);

        // Most-recent first - visually matches the widget popover (newest at top).
        return items
            .OrderByDescending(t => t.CreatedAt)
            .Take(200)
            .Select(MapToDto)
            .ToList();
    }

    // A task can only run if an agent on the target server is polling to claim it. Refuse to queue
    // one for an offline server so it does not sit Pending forever with no logs (the apache2-install
    // confusion). The server-management collaborator and the agent's own paths bypass this on purpose.
    private async Task EnsureServerOnlineAsync(int serverId, CancellationToken ct)
    {
        var status = await repo.GetServerStatusAsync(serverId, ct).ConfigureAwait(false);
        if (status is not null && status != ServerStatus.Online)
            throw new ConflictException(
                "The target server's agent is offline, so this task cannot run. Reconnect the agent and try again.");
    }

    public async Task<ServerTaskDto> CreateTaskAsync(CreateTaskRequest request, CancellationToken ct = default)
    {
        await EnsureServerOnlineAsync(request.ServerId, ct).ConfigureAwait(false);
        var task = new ServerTask
        {
            ServerId = request.ServerId,
            Name = request.Name,
            Command = request.Command,
            Executor = request.Executor,
            TimeoutSeconds = request.TimeoutSeconds,
            EnvironmentVariables = TaskEnvProtection.Protect(encryption, JsonSerializer.Serialize(request.EnvironmentVariables))
        };
        task = await repo.AddTaskAsync(task, ct).ConfigureAwait(false);
        await audit.LogAsync("Created", "Task", task.Id, task.Name, ct).ConfigureAwait(false);
        var dto = MapToDto(task);
        await BroadcastTaskQueuedAsync(dto, ct).ConfigureAwait(false);
        return dto;
    }

    public async Task<ServerTaskDto> CreateOperationAsync(CreateOperationRequest request, CancellationToken ct = default)
    {
        await EnsureServerOnlineAsync(request.ServerId, ct).ConfigureAwait(false);

        // F-32: typed operations carry their target string in `Command`. The agent's
        // IOperationExecutor validates the target against a per-kind regex, so no
        // shell allow-list is involved.
        var task = new ServerTask
        {
            ServerId = request.ServerId,
            Name = request.Name,
            Command = request.Target,
            Executor = ExecutorType.Operation, // Hardening (#30): typed operation, not shell
            Operation = request.Operation,
            TimeoutSeconds = request.TimeoutSeconds,
            EnvironmentVariables = "{}"
        };
        task = await repo.AddTaskAsync(task, ct).ConfigureAwait(false);
        await audit.LogAsync("CreatedOperation", "Task", task.Id, $"{request.Operation}:{request.Target}", ct).ConfigureAwait(false);
        var dto = MapToDto(task);
        await BroadcastTaskQueuedAsync(dto, ct).ConfigureAwait(false);
        return dto;
    }

    // Broadcast hook for tasks queued outside this service (ServerServiceManager's service-action and
    // package install/uninstall paths persist straight through the repository). Maps the same way and
    // reuses the same per-server/all-servers fan-out so the top-bar tracker treats them identically.
    // serverNameOverride: callers whose task entity has no Server navigation loaded (e.g. bulk
    // queuing from id/name pairs) pass the name explicitly so the tracker line stays readable.
    public Task NotifyTaskQueuedAsync(ServerTask task, string? serverNameOverride = null, CancellationToken ct = default)
    {
        var dto = MapToDto(task);
        if (!string.IsNullOrEmpty(serverNameOverride) && string.IsNullOrEmpty(dto.ServerName))
            dto = dto with { ServerName = serverNameOverride };
        return BroadcastTaskQueuedAsync(dto, ct);
    }

    // Item #7: surface every newly-queued task to the top-bar widget. The server hub already
    // filters by RBAC at JoinAllServers time, so we just broadcast on the per-server group -
    // only users with Read access to the target server actually receive the event.
    //
    // The broadcast is best-effort: the task is already persisted by the time we get here, so a
    // SignalR fan-out failure must NOT bubble up and fail the caller's request (it would leave the
    // task in the DB but report an error to the user, orphaned from the UI's point of view). Swallow
    // and log; the widget reconciles on its next poll / reconnect.
    private async Task BroadcastTaskQueuedAsync(ServerTaskDto dto, CancellationToken ct)
    {
        try
        {
            await Task.WhenAll(
                serverHub.Clients.Group(HubGroups.Server(dto.ServerId))
                    .SendAsync("TaskQueued", dto, ct),
                serverHub.Clients.Group(HubGroups.AllServers)
                    .SendAsync("TaskQueued", dto, ct)
            ).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "TaskQueued broadcast failed for task {TaskId} on server {ServerId}; the task is persisted and will reconcile on the next widget refresh.", dto.Id, dto.ServerId);
        }
    }

    public async Task<List<PendingTaskDto>> GetPendingTasksAsync(int serverId, int? take = null, CancellationToken ct = default)
    {
        var tasks = await repo.ClaimPendingTasksAsync(serverId, take, ct).ConfigureAwait(false);

        return tasks.Select(t => new PendingTaskDto
        {
            Id = t.Id,
            PipelineRunId = t.PipelineRunId,
            Name = t.Name,
            Command = t.Command,
            Executor = t.Executor,
            Operation = t.Operation,
            TimeoutSeconds = t.TimeoutSeconds,
            EnvironmentVariables = DeserializeEnv(TaskEnvProtection.Unprotect(encryption, t.EnvironmentVariables)),
            Container = t.ContainerImage is null ? null : new ContainerSpec
            {
                Image = t.ContainerImage,
                Runtime = t.ContainerRuntime,
                Network = t.ContainerNetwork,
                Memory = t.ContainerMemory,
                Cpus = t.ContainerCpus,
                WorkspaceKey = t.PipelineRunId ?? t.Id,
                // Only the run's system cleanup step purges the host workspace dir. IsSystem can't be
                // set by user steps, so a user step named "Cleanup" can never trigger a mid-run purge.
                PurgeWorkspace = t.PipelineStepRun is { IsSystem: true, StepName: "Cleanup" }
            }
        }).ToList();
    }

    public async Task<bool> StartTaskAsync(int id, CancellationToken ct = default)
    {
        var task = await repo.FindTaskAsync(id, ct).ConfigureAwait(false);
        if (task is null || task.Status is not (TaskExecutionStatus.Pending or TaskExecutionStatus.Assigned))
            return false;

        task.Status = TaskExecutionStatus.Running;
        task.StartedAt = timeProvider.GetUtcNow().UtcDateTime;

        if (task.PipelineStepRunId.HasValue)
        {
            var stepRun = await repo.FindPipelineStepRunAsync(task.PipelineStepRunId.Value, ct).ConfigureAwait(false);
            if (stepRun is not null)
            {
                stepRun.StartedAt = task.StartedAt;
                stepRun.Status = TaskExecutionStatus.Running;
            }
        }

        await repo.SaveChangesAsync(ct).ConfigureAwait(false);
        await Task.WhenAll(
            serverHub.Clients.Group(HubGroups.Server(task.ServerId))
                .SendAsync("TaskStarted", new { TaskId = task.Id, ServerId = task.ServerId, StartedAt = task.StartedAt }, ct),
            serverHub.Clients.Group(HubGroups.AllServers)
                .SendAsync("TaskStarted", new { TaskId = task.Id, ServerId = task.ServerId, StartedAt = task.StartedAt }, ct)
        ).ConfigureAwait(false);

        if (task.PipelineRunId.HasValue)
            await pipelineHub.Clients.Group($"pipeline-run-{task.PipelineRunId}")
                .SendAsync("StepStarted", task.PipelineStepRunId, ct).ConfigureAwait(false);

        return true;
    }

    public async Task<bool> CompleteTaskAsync(int id, TaskResultDto result, CancellationToken ct = default)
    {
        var task = await repo.FindTaskAsync(id, ct).ConfigureAwait(false);
        if (task is null || !TerminalTaskStatuses.Contains(result.Status)) return false;

        // Compatibility guard for agents that predate the current lifecycle contract: an old agent
        // can discover that a newly introduced operation is unsupported and report a terminal failure
        // while the task is still Assigned. Accept only a non-success terminal result from Assigned;
        // successful work must still have passed through StartTaskAsync/Running.
        var canComplete = task.Status == TaskExecutionStatus.Running
            || (task.Status == TaskExecutionStatus.Assigned && result.Status != TaskExecutionStatus.Success);
        if (!canComplete) return false;

        task.Status = result.Status;
        task.ExitCode = result.ExitCode;
        task.CompletedAt = timeProvider.GetUtcNow().UtcDateTime;

        // Capture the deploy artifact id BEFORE the env is scrubbed below - a successful PipelineDeploy
        // closes the loop (retention + ReleaseStatus.Deployed) once the task is persisted.
        var deployClosure = TryReadDeployClosure(task, result.Status);

        task.EnvironmentVariables = TaskEnvProtection.EmptyEnv; // F-001: scrub secrets at terminal state

        // Update pipeline step run if linked - keep a single fetch reused below for stage advance.
        PipelineStepRun? stepRun = null;
        if (task.PipelineStepRunId.HasValue)
        {
            stepRun = await repo.FindPipelineStepRunAsync(task.PipelineStepRunId.Value, ct).ConfigureAwait(false);
            if (stepRun is not null)
            {
                stepRun.Status = result.Status;
                stepRun.ExitCode = result.ExitCode;
                stepRun.CompletedAt = timeProvider.GetUtcNow().UtcDateTime;

                var logOutput = result.Output;
                if (string.IsNullOrEmpty(logOutput))
                {
                    // The agent never sends result.Output: fetch ONLY the ##aetheus[
                    // marker lines (SQL-side LIKE) instead of re-materialising up to 50k
                    // log lines of a verbose build at every step completion.
                    var markerLines = await logService.GetTaskOutputVariableLinesAsync(id, ct).ConfigureAwait(false);
                    logOutput = markerLines.Count > 0 ? string.Join("\n", markerLines) : null;
                }
                if (!string.IsNullOrEmpty(logOutput))
                {
                    var outputVars = PipelineRunHelpers.ParseOutputVariablesFromLogs(logOutput);
                    if (outputVars.Count > 0)
                        stepRun.OutputVariablesJson = System.Text.Json.JsonSerializer.Serialize(outputVars);
                }
            }
        }

        await repo.SaveChangesAsync(ct).ConfigureAwait(false);
        // From this point the agent result is durable and the encrypted retry context is scrubbed.
        // Finish the state machine even if the HTTP caller disconnects; otherwise a terminal task can
        // never be submitted again and its pipeline run remains permanently active.
        var completionCt = CancellationToken.None;

        // Deploy-success closure: flag the exact deployed release and retain its artifact. A bookkeeping
        // failure must become a terminal failed step; otherwise the already-scrubbed terminal task could
        // never be retried and the pipeline would remain stuck forever.
        if (deployClosure is { } closure)
        {
            try
            {
                var marked = await artifactService.MarkDeployedAsync(
                    closure.ArtifactId, closure.Cohort, closure.ReleaseId, completionCt).ConfigureAwait(false);
                if (!marked)
                    throw new InvalidOperationException($"Deployed artifact {closure.ArtifactId} no longer exists.");
                if (closure.RollbackId is { } rollbackId && domainEvents is not null)
                    await domainEvents.DispatchAsync(
                        new RollbackDeploymentSucceededEvent(rollbackId), completionCt).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Deploy closure failed for task {TaskId}, artifact {ArtifactId}, release {ReleaseId}",
                    task.Id, closure.ArtifactId, closure.ReleaseId);
                task.Status = TaskExecutionStatus.Failed;
                task.ExitCode = -1;
                if (stepRun is not null)
                {
                    stepRun.Status = TaskExecutionStatus.Failed;
                    stepRun.ExitCode = -1;
                }
                await repo.SaveChangesAsync(completionCt).ConfigureAwait(false);
            }
        }

        // Advance the authoritative state machine before best-effort UI notifications. A SignalR
        // outage must not strand a terminal task between stages.
        if (task.PipelineRunId.HasValue && stepRun is not null)
            await pipelineRunService.AdvanceStageAsync(
                task.PipelineRunId.Value, stepRun.StageName, completionCt).ConfigureAwait(false);
        // S-TECH-ARCR: a post-stage artifact-collection task carries the run id but no step run; its
        // completion is the signal to advance to the next stage (which AdvanceStageAsync deferred).
        else if (task.PipelineRunId.HasValue && task.Operation == OperationKind.PipelineCollectArtifacts)
            await pipelineRunService.ContinueAfterArtifactCollectionAsync(
                task.PipelineRunId.Value, task.Status, completionCt).ConfigureAwait(false);

        // Broadcast task completion to server group + all-servers (admin widget).
        var notification = new TaskCompletedNotification
        {
            TaskId = task.Id,
            ServerId = task.ServerId,
            TaskName = task.Name,
            Status = task.Status,
            ExitCode = task.ExitCode,
            Output = null
        };
        try
        {
            await Task.WhenAll(
                serverHub.Clients.Group(HubGroups.Server(task.ServerId))
                    .SendAsync("TaskCompleted", notification, completionCt),
                serverHub.Clients.Group(HubGroups.AllServers)
                    .SendAsync("TaskCompleted", notification, completionCt)
            ).ConfigureAwait(false);

            if (task.PipelineRunId.HasValue)
                await pipelineHub.Clients.Group($"pipeline-run-{task.PipelineRunId}")
                    .SendAsync("StepCompleted", task.PipelineStepRunId, task.Status, completionCt).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Task completion notification failed for task {TaskId}", task.Id);
        }

        return true;
    }

    // A successful deploy task carries the artifact id + app name in its (still-encrypted) env; decode
    // them so the caller can close the deploy loop after the env is scrubbed. The app name is the
    // deployment cohort key for retention (all deploys of app X age out the previous one). Null unless
    // this is a PipelineDeploy task that actually succeeded.
    private DeployClosure? TryReadDeployClosure(ServerTask task, TaskExecutionStatus status)
    {
        if (task.Operation != OperationKind.PipelineDeploy || status != TaskExecutionStatus.Success) return null;
        try
        {
            var json = TaskEnvProtection.Unprotect(encryption, task.EnvironmentVariables);
            var env = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (env is null
                || !env.TryGetValue("AETHEUS_DEPLOY_ARTIFACT_ID", out var idStr)
                || !int.TryParse(idStr, out var artifactId))
                return null;
            var cohort = env.TryGetValue("AETHEUS_DEPLOY_APP", out var app) && !string.IsNullOrWhiteSpace(app)
                ? app
                : task.Command;
            var rollbackId = env.TryGetValue("AETHEUS_ROLLBACK_ID", out var rollbackIdText)
                && int.TryParse(rollbackIdText, out var parsedRollbackId)
                ? parsedRollbackId
                : (int?)null;
            var releaseId = env.TryGetValue("AETHEUS_DEPLOY_RELEASE_ID", out var releaseIdText)
                && int.TryParse(releaseIdText, out var parsedReleaseId)
                ? parsedReleaseId
                : (int?)null;
            return new DeployClosure(artifactId, cohort, rollbackId, releaseId);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Cohort = the deployment cohort key (app name), NOT a promote environment. It is stored on the
    // artifact under RetentionPolicy.Deployed, which keeps it separate from promote environments even
    // when an app happens to be named like one.
    private readonly record struct DeployClosure(int ArtifactId, string Cohort, int? RollbackId, int? ReleaseId);

    private static readonly TaskExecutionStatus[] TerminalTaskStatuses =
        [TaskExecutionStatus.Success, TaskExecutionStatus.Failed, TaskExecutionStatus.Cancelled, TaskExecutionStatus.Timeout];

    public async Task<bool> CancelTaskAsync(int id, CancellationToken ct = default)
    {
        var task = await repo.FindTaskAsync(id, ct).ConfigureAwait(false);
        // Reject EVERY terminal status (not just Success/Failed): a task already Cancelled or
        // Timeout (e.g. just swept by TaskTimeoutService) must not be re-cancelled - that would
        // re-broadcast StepCompleted and re-enter AdvanceStageAsync, double-advancing the stage.
        if (task is null || TerminalTaskStatuses.Contains(task.Status))
            return false;

        task.Status = TaskExecutionStatus.Cancelled;
        task.CompletedAt = timeProvider.GetUtcNow().UtcDateTime;
        task.EnvironmentVariables = TaskEnvProtection.EmptyEnv; // F-001: scrub secrets at terminal state

        // Mirror CompleteTaskAsync's pipeline plumbing: without it, cancelling a step's task
        // via the Tasks API left the PipelineStepRun Running and the run stuck In Progress,
        // with no SignalR notification to the top-bar tracker or the run view.
        // stepTransitioned = did THIS cancel actually move the step to terminal? If the step was
        // already completed (by a racing CompleteTaskAsync), we must NOT re-broadcast or re-advance.
        PipelineStepRun? stepRun = null;
        var stepTransitioned = false;
        if (task.PipelineStepRunId.HasValue)
        {
            stepRun = await repo.FindPipelineStepRunAsync(task.PipelineStepRunId.Value, ct).ConfigureAwait(false);
            if (stepRun is not null && stepRun.CompletedAt is null)
            {
                stepRun.Status = TaskExecutionStatus.Cancelled;
                stepRun.CompletedAt = timeProvider.GetUtcNow().UtcDateTime;
                stepTransitioned = true;
            }
        }

        await repo.SaveChangesAsync(ct).ConfigureAwait(false);

        var notification = new TaskCompletedNotification
        {
            TaskId = task.Id,
            ServerId = task.ServerId,
            TaskName = task.Name,
            Status = TaskExecutionStatus.Cancelled,
            ExitCode = task.ExitCode,
            Output = null
        };
        await Task.WhenAll(
            serverHub.Clients.Group(HubGroups.Server(task.ServerId))
                .SendAsync("TaskCompleted", notification, ct),
            serverHub.Clients.Group(HubGroups.AllServers)
                .SendAsync("TaskCompleted", notification, ct)
        ).ConfigureAwait(false);

        // Only touch the pipeline run when THIS call actually transitioned the step - otherwise a
        // concurrent completion already broadcast StepCompleted and advanced the stage.
        if (task.PipelineRunId.HasValue && stepTransitioned)
        {
            await pipelineHub.Clients.Group($"pipeline-run-{task.PipelineRunId}")
                .SendAsync("StepCompleted", task.PipelineStepRunId, TaskExecutionStatus.Cancelled, ct).ConfigureAwait(false);
            await pipelineRunService.AdvanceStageAsync(task.PipelineRunId.Value, stepRun!.StageName, ct).ConfigureAwait(false);
        }
        else if (task.PipelineRunId.HasValue && stepRun is null && task.Operation == OperationKind.PipelineCollectArtifacts)
        {
            await pipelineRunService.ContinueAfterArtifactCollectionAsync(
                task.PipelineRunId.Value, TaskExecutionStatus.Cancelled, ct).ConfigureAwait(false);
        }

        return true;
    }

    public async Task<int?> GetTaskServerIdAsync(int taskId, CancellationToken ct = default)
    {
        var task = await repo.FindTaskAsync(taskId, ct).ConfigureAwait(false);
        return task?.ServerId;
    }

    public Task<Dictionary<int, int>> GetServerIdsForTasksAsync(IReadOnlyCollection<int> taskIds, CancellationToken ct = default) =>
        repo.GetServerIdsForTasksAsync(taskIds, ct);

    public async Task<Dictionary<int, string>> GetTaskStatusesAsync(List<int> taskIds, int serverId, CancellationToken ct = default)
    {
        if (taskIds.Count == 0) return new();
        return await repo.GetTaskStatusesAsync(taskIds, serverId, ct).ConfigureAwait(false);
    }

    private static ServerTaskDto MapToDto(ServerTask t) => new()
    {
        Id = t.Id,
        ServerId = t.ServerId,
        ServerName = t.Server?.Name ?? string.Empty,
        ServerStatus = t.Server?.Status ?? ServerStatus.Online,
        Name = t.Name,
        Command = "[masked]",
        Executor = t.Executor,
        Status = t.Status,
        PipelineRunId = t.PipelineRunId,
        PipelineStepRunId = t.PipelineStepRunId,
        CreatedAt = t.CreatedAt,
        StartedAt = t.StartedAt,
        CompletedAt = t.CompletedAt,
        ExitCode = t.ExitCode,
        TimeoutSeconds = t.TimeoutSeconds
    };

    private static Dictionary<string, string> DeserializeEnv(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? []; }
        // Malformed env JSON in legacy rows degrades to empty env (intentional, validated on write).
        catch (JsonException) { return []; }
    }
}
