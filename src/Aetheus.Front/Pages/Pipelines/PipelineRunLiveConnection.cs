// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Pipelines;

/// <summary>
/// Owns the pipelines-hub connection for a run view: it joins the run's group (plus every loaded child
/// run group of an orchestration) and turns a StepStarted/StepCompleted broadcast into a debounced
/// reload of the parent component. Extracted from <see cref="PipelineRun"/> to keep that component under
/// the file-size budget; the component keeps the actual reload (<c>onReload</c>) since it mutates the
/// view state, and supplies the current child-run ids (<c>childRunIds</c>) so newly-triggered children
/// are joined on the next reload.
/// </summary>
internal sealed class PipelineRunLiveConnection(
    HubConnectionFactory hubFactory,
    ILogger logger,
    int runId,
    Func<IReadOnlyCollection<int>> childRunIds,
    Func<Task> onReload,
    Func<Task> onQueueRefresh,
    Func<Func<Task>, Task> invokeAsync,
    Func<bool>? needsQueuePolling = null) : IAsyncDisposable
{
    private HubConnection? _hub;
    // Run groups already joined: the parent run plus every loaded child run of an orchestration. A child
    // pipeline broadcasts its step events to ITS OWN run group, so without joining the children the parent
    // view only refreshed on a manual reload.
    private readonly HashSet<int> _joined = [];
    private readonly DebouncedAsyncAction _reload = new(onReload, logger, "[PipelineRun]");
    private Task? _queuePollTask;
    private bool _disposed;

    public async Task StartAsync()
    {
        if (_disposed) return;
        _queuePollTask ??= PollQueueAsync();
        _hub = hubFactory.Create("pipelines");
        _hub.On<int, int>("PipelineRunStarted", (_, _) => invokeAsync(ScheduleReloadAsync));
        _hub.On<int, PipelineStatus>("PipelineRunCompleted", (_, _) => invokeAsync(ScheduleReloadAsync));
        _hub.On<int>("PipelineRunCancelled", _ => invokeAsync(ScheduleReloadAsync));
        _hub.On<int>("StepStarted", HandleStepStartedAsync);
        _hub.On<int, TaskExecutionStatus>("StepCompleted", (_, _) => invokeAsync(ScheduleReloadAsync));
        _hub.On<int, string, string>("ApprovalRequired", (_, _, _) => invokeAsync(ScheduleReloadAsync));
        _hub.On<int, string, string>("ApprovalResolved", (_, _, _) => invokeAsync(ScheduleReloadAsync));
        // Group membership is per-connection and lost on auto-reconnect - re-join every run group + reload.
        _hub.RejoinOnReconnect(() => invokeAsync(async () =>
        {
            if (_disposed) return;
            _joined.Clear();
            await JoinGroupsAsync();
            await ScheduleReloadAsync();
        }));

        try
        {
            await _hub.StartAsync();
            await JoinGroupsAsync();
        }
        catch { /* Hub unavailable - degrade to static */ }
    }

    private async Task PollQueueAsync()
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
            while (await timer.WaitForNextTickAsync(_reload.LifetimeToken))
            {
                await invokeAsync(() => needsQueuePolling?.Invoke() == true
                    ? onQueueRefresh()
                    : Task.CompletedTask);
            }
        }
        catch (OperationCanceledException) when (_reload.LifetimeToken.IsCancellationRequested) { }
        catch (Exception ex) { logger.LogWarning(ex, "[PipelineRun] Queue polling failed"); }
    }

    // Join the parent run group plus every currently-loaded child run group. Idempotent: only groups not
    // already joined are (re)joined. Call after each reload, since a triggered child run only appears once
    // its trigger step has fired.
    public async Task JoinGroupsAsync()
    {
        if (_disposed || _hub is not { State: HubConnectionState.Connected }) return;
        var desired = new HashSet<int>(DesiredRunIds());
        foreach (var obsoleteId in _joined.Where(id => !desired.Contains(id)).ToList())
        {
            try
            {
                await _hub.InvokeAsync("LeavePipelineRunGroup", obsoleteId);
                _joined.Remove(obsoleteId);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "[PipelineRun] Leave obsolete run group {Id} failed", obsoleteId);
            }
        }
        foreach (var id in desired)
        {
            if (!_joined.Add(id)) continue;
            try { await _hub.InvokeAsync("JoinPipelineRunGroup", id); }
            catch (Exception ex) { _joined.Remove(id); logger.LogDebug(ex, "[PipelineRun] Join run group {Id} failed", id); }
        }
    }

    internal Task HandleStepStartedAsync(int stepId)
    {
        _ = stepId;
        return invokeAsync(ScheduleReloadAsync);
    }

    internal IReadOnlyCollection<int> DesiredRunIds() =>
        new[] { runId }.Concat(childRunIds()).Distinct().ToArray();

    // Debounce: a burst of step events collapses to a single reload 250 ms after the last one.
    private Task ScheduleReloadAsync() => _reload.ScheduleAsync();

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _reload.DisposeAsync();
        if (_queuePollTask is not null)
            await _queuePollTask;

        if (_hub is not null)
        {
            foreach (var joinedRunId in _joined.ToList())
            {
                try { await _hub.InvokeAsync("LeavePipelineRunGroup", joinedRunId); }
                catch (Exception ex) { logger.LogDebug(ex, "[PipelineRun] Leave run group {Id} failed", joinedRunId); }
            }
            _joined.Clear();
            await _hub.DisposeAsync();
        }

    }
}
