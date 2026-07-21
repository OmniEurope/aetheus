// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Services;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.SignalR.Client;

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
    Func<Func<Task>, Task> invokeAsync) : IAsyncDisposable
{
    private HubConnection? _hub;
    // Run groups already joined: the parent run plus every loaded child run of an orchestration. A child
    // pipeline broadcasts its step events to ITS OWN run group, so without joining the children the parent
    // view only refreshed on a manual reload.
    private readonly HashSet<int> _joined = [];
    private readonly object _reloadSync = new();
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly SemaphoreSlim _reloadGate = new(1, 1);
    private CancellationTokenSource? _reloadCts;
    private Task? _reloadTask;
    private bool _disposed;

    public async Task StartAsync()
    {
        if (_disposed) return;
        _hub = hubFactory.Create("pipelines");
        _hub.On<int, int>("PipelineRunStarted", (_, _) => invokeAsync(ScheduleReloadAsync));
        _hub.On<int, PipelineStatus>("PipelineRunCompleted", (_, _) => invokeAsync(ScheduleReloadAsync));
        _hub.On<int>("PipelineRunCancelled", _ => invokeAsync(ScheduleReloadAsync));
        _hub.On<int>("StepStarted", _ => invokeAsync(ScheduleReloadAsync));
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

    // Join the parent run group plus every currently-loaded child run group. Idempotent: only groups not
    // already joined are (re)joined. Call after each reload, since a triggered child run only appears once
    // its trigger step has fired.
    public async Task JoinGroupsAsync()
    {
        if (_disposed || _hub is not { State: HubConnectionState.Connected }) return;
        foreach (var id in new[] { runId }.Concat(childRunIds()))
        {
            if (!_joined.Add(id)) continue;
            try { await _hub.InvokeAsync("JoinPipelineRunGroup", id); }
            catch (Exception ex) { _joined.Remove(id); logger.LogDebug(ex, "[PipelineRun] Join run group {Id} failed", id); }
        }
    }

    // Debounce: a burst of step events collapses to a single reload 250 ms after the last one.
    private async Task ScheduleReloadAsync()
    {
        lock (_reloadSync)
        {
            if (_disposed) return;
            _reloadCts?.Cancel();
            var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
            _reloadCts = cts;
            _reloadTask = ReloadAfterDelayAsync(cts);
        }
        await Task.CompletedTask;
    }

    private async Task ReloadAfterDelayAsync(CancellationTokenSource cts)
    {
        var entered = false;
        try
        {
            await Task.Delay(250, cts.Token);
            await _reloadGate.WaitAsync(cts.Token);
            entered = true;
            if (_disposed) return;
            await onReload();
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (Exception ex) { logger.LogWarning(ex, "[PipelineRun] Live reload failed"); }
        finally
        {
            if (entered) _reloadGate.Release();
            lock (_reloadSync)
            {
                if (ReferenceEquals(_reloadCts, cts))
                {
                    _reloadCts = null;
                    _reloadTask = null;
                }
            }
            cts.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task? pendingReload;
        lock (_reloadSync)
        {
            if (_disposed) return;
            _disposed = true;
            _lifetimeCts.Cancel();
            _reloadCts?.Cancel();
            pendingReload = _reloadTask;
        }
        if (pendingReload is not null)
            await pendingReload;

        await _reloadGate.WaitAsync();
        _reloadGate.Release();

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

        _lifetimeCts.Dispose();
        _reloadGate.Dispose();
    }
}
