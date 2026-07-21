// SPDX-License-Identifier: EUPL-1.2
using System.Collections.Concurrent;
using Aetheus.Agent.Core.Configuration;
using Aetheus.Agent.Core.Executors;
using Aetheus.Agent.Core.Operations;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.Extensions.Options;

namespace Aetheus.Agent.Core.Services;

public sealed class PollingService(
    IServerApiClient apiClient,
    IEnrollmentService enrollment,
    IEnumerable<IExecutor> executors,
    IEnumerable<IOperationExecutor> operationExecutors,
    IContainerExecutor containerExecutor,
    IDockerStorageMaintenance dockerStorageMaintenance,
    AgentState agentState,
    AgentRuntimeHealth runtimeHealth,
    IAgentProcessRestarter processRestarter,
    TimeProvider timeProvider,
    IOptions<AetheusAgentOptions> options,
    ILogger<PollingService> logger) : BackgroundService
{
    private readonly AetheusAgentOptions _options = options.Value;
    private readonly SemaphoreSlim _concurrencyGate = new(options.Value.MaxConcurrentTasks);
    internal readonly ConcurrentDictionary<int, TrackedTask> _runningTasks = new();

    private readonly ConcurrentDictionary<OperationKind, IOperationExecutor?> _operationExecutorCache = new();

    internal sealed record TrackedTask(Task Execution, CancellationTokenSource Cts);

    private static readonly HashSet<string> TerminalStatuses =
        [nameof(TaskExecutionStatus.Success), nameof(TaskExecutionStatus.Failed),
         nameof(TaskExecutionStatus.Cancelled), nameof(TaskExecutionStatus.Timeout)];

    public override void Dispose()
    {
        _concurrencyGate.Dispose();
        base.Dispose();
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);

        if (!_runningTasks.IsEmpty)
        {
            logger.LogInformation("Waiting for {Count} in-flight tasks to complete...", _runningTasks.Count);

            var drainTask = Task.WhenAll(_runningTasks.Values.Select(t => t.Execution));
            var shutdownTcs = new TaskCompletionSource();
            await using (cancellationToken.Register(() => shutdownTcs.TrySetResult()).ConfigureAwait(false))
            {
                var winner = await Task.WhenAny(drainTask, shutdownTcs.Task).ConfigureAwait(false);
                if (winner == drainTask)
                    logger.LogInformation("All in-flight tasks drained");
                else
                    logger.LogWarning("Shutdown budget exhausted with {Count} task(s) still running", _runningTasks.Count);
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!enrollment.IsEnrolled && !stoppingToken.IsCancellationRequested)
            await Task.Delay(AgentRuntimeDefaults.StartupRetryDelay, timeProvider, stoppingToken).ConfigureAwait(false);

        runtimeHealth.MarkPollingProgress();
        logger.LogInformation("Polling service started (interval: {Interval}s, maxConcurrent: {Max})",
            _options.PollingIntervalSeconds, _options.MaxConcurrentTasks);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.PollingIntervalSeconds));

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            runtimeHealth.MarkPollingProgress();
            try
            {
                await PollOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Polling failed, will retry next interval");
            }
        }
    }

    // E-3: one poll iteration's work (reconcile + fetch + dispatch), extracted so tests can
    // drive it deterministically without a PeriodicTimer or a real Task.Delay sleep. Dispatch
    // stays fire-and-forget (tracked in _runningTasks); tests await DrainRunningTasksAsync to
    // observe the dispatched task's terminal effect deterministically.
    internal async Task PollOnceAsync(CancellationToken stoppingToken)
    {
        await ReconcileRunningTasksAsync(stoppingToken).ConfigureAwait(false);

        var freeSlots = Math.Max(0, _options.MaxConcurrentTasks - _runningTasks.Count);
        var tasks = await apiClient.GetPendingTasksAsync(agentState.ServerId!.Value, freeSlots, stoppingToken).ConfigureAwait(false);

        foreach (var task in tasks)
        {
            if (_runningTasks.ContainsKey(task.Id))
                continue;

            var bypassGate = task.Operation == OperationKind.AgentSelfUpdate;

            if (!bypassGate && !await _concurrencyGate.WaitAsync(0, stoppingToken).ConfigureAwait(false))
            {
                logger.LogDebug("Max concurrent tasks reached, deferring remaining to next poll");
                break;
            }

            var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            // The tracking entry must exist BEFORE execution starts: a synchronously
            // completing execution would otherwise finish before TryAdd, leaving a ghost
            // entry whose disposed CTS then throws on every reconciliation pass.
            var registered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var executionTask = TrackTaskAsync(task, cts, registered.Task, bypassGate);
            _runningTasks.TryAdd(task.Id, new TrackedTask(executionTask, cts));
            registered.SetResult();
        }
    }

    // E-3 test seam: await all currently dispatched in-flight tasks so an assertion on a
    // task's terminal effect (StartTask/CompleteTask) is deterministic rather than time-raced.
    internal Task DrainRunningTasksAsync() =>
        Task.WhenAll(_runningTasks.Values.Select(t => t.Execution));

    internal async Task ReconcileRunningTasksAsync(CancellationToken ct)
    {
        if (_runningTasks.IsEmpty) return;

        var trackedIds = _runningTasks.Keys.ToList();
        try
        {
            var statuses = await apiClient.GetTaskStatusesAsync(trackedIds, ct).ConfigureAwait(false);
            foreach (var (taskId, status) in statuses)
            {
                if (!TerminalStatuses.Contains(status)) continue;
                if (!_runningTasks.TryGetValue(taskId, out var tracked)) continue;

                logger.LogWarning("Task {TaskId} was {Status} server-side - cancelling local execution", taskId, status);
                await tracked.Cts.CancelAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Task reconciliation failed (non-fatal), will retry next poll");
        }
    }

    private async Task TrackTaskAsync(PendingTaskDto task, CancellationTokenSource cts, Task registered, bool bypassedGate = false)
    {
        await registered.ConfigureAwait(false);
        try
        {
            await ExecuteTaskAsync(task, cts.Token, bypassedGate).ConfigureAwait(false);
        }
        finally
        {
            _runningTasks.TryRemove(task.Id, out _);
            cts.Dispose();
        }
    }

    private async Task ExecuteTaskAsync(PendingTaskDto task, CancellationToken ct, bool bypassedGate = false)
    {
        try
        {
            logger.LogInformation("Starting task {TaskId}: {Name}{Priority}", task.Id, task.Name,
                bypassedGate ? " [PRIORITY - bypassed concurrency gate]" : "");

            task.EnvironmentVariables["AETHEUS_AGENT_WORK_DIRECTORY"] = _options.WorkDirectory;
            RehomeMirrorCloneUrls(task.EnvironmentVariables);

            task.EnvironmentVariables.TryGetValue("AETHEUS_EXECUTION_ROLE", out var executionRole);
            if (task.PipelineRunId.HasValue
                && dockerStorageMaintenance.DeploymentOnly
                && !string.Equals(executionRole, "deploy", StringComparison.OrdinalIgnoreCase))
            {
                await apiClient.StartTaskAsync(task.Id, ct).ConfigureAwait(false);
                throw new InvalidOperationException(
                    "Pipeline task refused: a deployment-only agent accepts only stages explicitly classified as deploy.");
            }

            if (task.Operation != OperationKind.None)
                await ExecuteOperationAsync(task, ct).ConfigureAwait(false);
            else if (task.Container is not null)
                await ExecuteContainerAsync(task, ct).ConfigureAwait(false);
            else
                await ExecuteShellAsync(task, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            logger.LogInformation("Task {TaskId} cancelled (server-side or shutdown)", task.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Task {TaskId} execution error", task.Id);
            await TryReportFailureAsync(task.Id, ct).ConfigureAwait(false);
        }
        finally
        {
            if (!bypassedGate)
                _concurrencyGate.Release();
        }
    }

    // Re-home the internal-mirror clone URLs onto THIS agent's own reachable backend base (its
    // ServerUrl) so the pipeline checkout clones from the endpoint the agent already reaches, with no
    // server-side GitLight:CloneBaseUrl config, on any topology. No-op for external (non-mirror) repos.
    private void RehomeMirrorCloneUrls(Dictionary<string, string> env)
    {
        foreach (var key in new[] { "REPOSITORY_URL", "BUILD_REPOSITORY_URI" })
        {
            if (env.TryGetValue(key, out var url))
            {
                var rehomed = MirrorUrlRehomer.RehomeToAgentBase(url, _options.ServerUrl);
                if (!string.Equals(rehomed, url, StringComparison.Ordinal))
                {
                    env[key] = rehomed;
                    // Never log the value - it may carry embedded git credentials.
                    logger.LogInformation("Re-homed {Key} clone authority onto the agent's ServerUrl base", key);
                }
            }
        }
    }

    private async Task ExecuteOperationAsync(PendingTaskDto task, CancellationToken ct)
    {
        // Transition the claimed task before resolving the local handler. Older agents may not know
        // a newly introduced typed operation; without this transition their failure report targets an
        // Assigned task and older backends reject it, leaving the pipeline stuck until its timeout.
        await apiClient.StartTaskAsync(task.Id, ct).ConfigureAwait(false);

        var operationExecutor = _operationExecutorCache.GetOrAdd(
            task.Operation,
            kind => operationExecutors.FirstOrDefault(e => e.CanHandle(kind)));

        if (operationExecutor is null)
        {
            logger.LogError("No operation executor handles {Operation}", task.Operation);
            await CompleteWithFailureAsync(task.Id, ct).ConfigureAwait(false);
            return;
        }

        await using var logForwarder = new BufferedLogForwarder(task.Id, apiClient, timeProvider, ct);

        var result = await operationExecutor.ExecuteAsync(
            task.Operation, task.Command, task.EnvironmentVariables, task.TimeoutSeconds, logForwarder.OnOutputAsync, ct).ConfigureAwait(false);

        try
        {
            await logForwarder.FlushAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Task {TaskId} log flush failed (result preserved)", task.Id);
        }

        var restartAfterSelfUpdate = task.Operation == OperationKind.AgentSelfUpdate
            && result.ExitCode == 0
            && !result.TimedOut;
        try
        {
            await CompleteTaskAsync(task.Id, result.ExitCode, result.TimedOut, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            // A graceful host shutdown is not a Windows service failure, so SCM may leave the
            // agent stopped forever. Prefer completing the backend task first, but never leave
            // swapped binaries running under the old process merely because the final control-
            // plane acknowledgement failed: the new agent can reconcile after reconnecting.
            if (restartAfterSelfUpdate)
            {
                logger.LogWarning("Agent self-update handoff completed; terminating for service-manager restart");
                processRestarter.Restart("Agent self-update applied; service-manager restart requested.");
            }
        }
    }

    private async Task ExecuteShellAsync(PendingTaskDto task, CancellationToken ct)
    {
        // Keep the server-side lifecycle valid even when this agent does not support the requested
        // executor: Assigned -> Running -> Failed, never Assigned -> Failed.
        await apiClient.StartTaskAsync(task.Id, ct).ConfigureAwait(false);

        var executor = executors.FirstOrDefault(e => e.Type == task.Executor);
        if (executor is null)
        {
            logger.LogError("No executor found for type {Type}", task.Executor);
            await CompleteWithFailureAsync(task.Id, ct).ConfigureAwait(false);
            return;
        }

        await using var logForwarder = new BufferedLogForwarder(task.Id, apiClient, timeProvider, ct);

        task.EnvironmentVariables.TryGetValue("AETHEUS_EXECUTION_ROLE", out var executionRole);
        var isDockerBuild = dockerStorageMaintenance.IsBuildCommand(task.Command);
        var isBuildWorkload = isDockerBuild
            || string.Equals(executionRole, "build", StringComparison.OrdinalIgnoreCase);
        if (isBuildWorkload)
            await dockerStorageMaintenance.PrepareBuildAsync(
                task.EnvironmentVariables, ct, runDockerMaintenance: isDockerBuild).ConfigureAwait(false);

        ExecutorResult result;
        try
        {
            result = await executor.ExecuteAsync(
                task.Command, task.EnvironmentVariables, task.TimeoutSeconds, logForwarder.OnOutputAsync, ct).ConfigureAwait(false);
        }
        finally
        {
            if (isBuildWorkload)
                await dockerStorageMaintenance.CompleteBuildAsync(
                    CancellationToken.None, runDockerMaintenance: isDockerBuild).ConfigureAwait(false);
        }

        try
        {
            await logForwarder.FlushAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Task {TaskId} log flush failed (build result preserved)", task.Id);
        }

        await CompleteTaskAsync(task.Id, result.ExitCode, result.TimedOut, CancellationToken.None).ConfigureAwait(false);

        logger.LogInformation("Task {TaskId} completed: exit code {ExitCode}", task.Id, result.ExitCode);
    }

    private async Task ExecuteContainerAsync(PendingTaskDto task, CancellationToken ct)
    {
        await apiClient.StartTaskAsync(task.Id, ct).ConfigureAwait(false);
        await using var logForwarder = new BufferedLogForwarder(task.Id, apiClient, timeProvider, ct);

        var result = await containerExecutor.ExecuteAsync(
            task.Container!, task.Command, task.EnvironmentVariables, task.TimeoutSeconds,
            logForwarder.OnOutputAsync, ct).ConfigureAwait(false);

        try
        {
            await logForwarder.FlushAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Task {TaskId} log flush failed (container result preserved)", task.Id);
        }

        await CompleteTaskAsync(task.Id, result.ExitCode, result.TimedOut, CancellationToken.None).ConfigureAwait(false);
        logger.LogInformation("Container task {TaskId} completed: exit code {ExitCode}", task.Id, result.ExitCode);
    }

    private async Task CompleteTaskAsync(int taskId, int exitCode, bool timedOut, CancellationToken ct)
    {
        var status = timedOut
            ? TaskExecutionStatus.Timeout
            : exitCode == 0 ? TaskExecutionStatus.Success : TaskExecutionStatus.Failed;

        await apiClient.CompleteTaskAsync(taskId, new TaskResultDto
        {
            TaskId = taskId,
            Status = status,
            ExitCode = exitCode
        }, ct).ConfigureAwait(false);
    }

    private async Task CompleteWithFailureAsync(int taskId, CancellationToken ct)
    {
        await apiClient.CompleteTaskAsync(taskId, new TaskResultDto
        {
            TaskId = taskId,
            Status = TaskExecutionStatus.Failed,
            ExitCode = -1
        }, ct).ConfigureAwait(false);
    }

    private async Task TryReportFailureAsync(int taskId, CancellationToken _)
    {
        try
        {
            await CompleteWithFailureAsync(taskId, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception innerEx)
        {
            logger.LogError(innerEx, "Failed to report task {TaskId} failure", taskId);
        }
    }

    private sealed class BufferedLogForwarder(int taskId, IServerApiClient apiClient, TimeProvider timeProvider, CancellationToken ct) : IAsyncDisposable
    {
        private readonly List<AppendLogRequest> _buffer = new();
        private readonly object _lock = new();
        private readonly CancellationTokenSource _timerCts = new();
        private Task? _timerLoop;
        private int _droppedLines;
        private const int BatchSize = 20;
        // Outage cap: beyond this, oldest lines are dropped (and honestly reported).
        private const int MaxBufferedLines = 2000;
        private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(1.5);

        public async Task OnOutputAsync(string message, TaskLogLevel level)
        {
            var log = new AppendLogRequest { TaskId = taskId, Level = level, Message = message };
            List<AppendLogRequest>? batch = null;
            lock (_lock)
            {
                // Lazily start the periodic flush on first output: a step slower than the
                // 20-line threshold would otherwise show nothing live until it finishes.
                _timerLoop ??= TimerLoopAsync();
                _buffer.Add(log);
                if (_buffer.Count >= BatchSize)
                    batch = TakeBufferLocked();
            }
            if (batch is not null)
                await TrySendAsync(batch, ct).ConfigureAwait(false);
        }

        public async Task FlushAsync(CancellationToken flushCt)
        {
            // Stop the periodic timer FIRST: otherwise a timer tick could TakeBuffer + send
            // concurrently with this terminal flush, delivering the run's last log lines out of
            // order (or emitting the dropped-count warning twice). After StopTimerAsync returns,
            // this flush is the sole remaining writer.
            await StopTimerAsync().ConfigureAwait(false);
            List<AppendLogRequest>? remaining;
            lock (_lock)
            {
                remaining = _buffer.Count > 0 || _droppedLines > 0 ? TakeBufferLocked() : null;
            }
            if (remaining is not null)
                await apiClient.AppendLogBatchAsync(remaining, flushCt).ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            await StopTimerAsync().ConfigureAwait(false);
            _timerCts.Dispose();
        }

        // Idempotent: cancels the periodic flush loop and awaits its exit. Safe to call from both
        // FlushAsync and DisposeAsync (the second call is a no-op once the loop has completed).
        private async Task StopTimerAsync()
        {
            if (!_timerCts.IsCancellationRequested)
                await _timerCts.CancelAsync().ConfigureAwait(false);
            if (_timerLoop is not null)
            {
                try
                {
                    await _timerLoop.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Normal timer shutdown.
                }
            }
        }

        private async Task TimerLoopAsync()
        {
            while (!_timerCts.IsCancellationRequested)
            {
                await Task.Delay(FlushInterval, timeProvider, _timerCts.Token).ConfigureAwait(false);
                List<AppendLogRequest>? pending;
                lock (_lock)
                {
                    pending = _buffer.Count > 0 ? TakeBufferLocked() : null;
                }
                if (pending is not null)
                    await TrySendAsync(pending, ct).ConfigureAwait(false);
            }
        }

        // Log forwarding is best-effort: a backend blip must never fail a real build.
        // Failed batches are re-queued (bounded) and retried by the next flush.
        private async Task TrySendAsync(List<AppendLogRequest> batch, CancellationToken token)
        {
            try
            {
                await apiClient.AppendLogBatchAsync(batch, token).ConfigureAwait(false);
            }
            catch (Exception)
            {
                lock (_lock)
                {
                    _buffer.InsertRange(0, batch);
                    var overflow = _buffer.Count - MaxBufferedLines;
                    if (overflow > 0)
                    {
                        _buffer.RemoveRange(0, overflow);
                        _droppedLines += overflow;
                    }
                }
            }
        }

        private List<AppendLogRequest> TakeBufferLocked()
        {
            List<AppendLogRequest> batch = [.. _buffer];
            _buffer.Clear();
            if (_droppedLines > 0)
            {
                batch.Insert(0, new AppendLogRequest
                {
                    TaskId = taskId,
                    Level = TaskLogLevel.Warning,
                    Message = $"[agent] {_droppedLines} log line(s) dropped while the backend was unreachable"
                });
                _droppedLines = 0;
            }
            return batch;
        }
    }
}
