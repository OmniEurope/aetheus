// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Services;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.SignalR.Client;

namespace Aetheus.Front.Pages.Pipelines;

/// <summary>
/// Owns the pipelines-hub subscription used by the pipeline edit page. It tracks the active run
/// groups, restores them after reconnect, and coalesces event bursts into one serialized reload.
/// </summary>
internal sealed class PipelineRunsLiveConnection(
    HubConnectionFactory hubFactory,
    ILogger logger,
    int pipelineId,
    Func<IReadOnlyCollection<int>> activeRunIds,
    Func<Task> onReload,
    Func<Func<Task>, Task> invokeAsync) : IAsyncDisposable
{
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly SemaphoreSlim _reloadGate = new(1, 1);
    private readonly object _reloadSync = new();
    private readonly HashSet<int> _joinedRunGroups = [];
    private HubConnection? _hub;
    private CancellationTokenSource? _reloadCts;
    private Task? _reloadTask;
    private bool _disposed;

    public async Task StartAsync()
    {
        if (_disposed) return;
        _hub = hubFactory.Create("pipelines");
        _hub.On<int, int>("PipelineRunStarted", (_, eventPipelineId) =>
            eventPipelineId == pipelineId ? invokeAsync(ScheduleReloadAsync) : Task.CompletedTask);
        _hub.On<int, PipelineStatus>("PipelineRunCompleted", (_, _) => invokeAsync(ScheduleReloadAsync));
        _hub.On<int>("PipelineRunCancelled", _ => invokeAsync(ScheduleReloadAsync));
        _hub.On<int>("StepStarted", _ => invokeAsync(ScheduleReloadAsync));
        _hub.On<int, TaskExecutionStatus>("StepCompleted", (_, _) => invokeAsync(ScheduleReloadAsync));
        _hub.On<int, string, string>("ApprovalRequired", (_, _, _) => invokeAsync(ScheduleReloadAsync));
        _hub.On<int, string, string>("ApprovalResolved", (_, _, _) => invokeAsync(ScheduleReloadAsync));
        _hub.RejoinOnReconnect(() => invokeAsync(async () =>
        {
            if (_disposed || _hub is null) return;
            await _hub.InvokeAsync("JoinPipelineUpdatesGroup");
            _joinedRunGroups.Clear();
            await SyncGroupsAsync();
            await ScheduleReloadAsync();
        }));

        try
        {
            await _hub.StartAsync();
            await _hub.InvokeAsync("JoinPipelineUpdatesGroup");
            await SyncGroupsAsync();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "[PipelineEdit] Live updates unavailable");
        }
    }

    public async Task SyncGroupsAsync()
    {
        if (_disposed || _hub is not { State: HubConnectionState.Connected }) return;
        var desired = activeRunIds().ToHashSet();
        foreach (var runId in _joinedRunGroups.Where(id => !desired.Contains(id)).ToList())
        {
            try { await _hub.InvokeAsync("LeavePipelineRunGroup", runId); }
            catch (Exception ex) { logger.LogDebug(ex, "[PipelineEdit] Leave run group {RunId} failed", runId); }
            _joinedRunGroups.Remove(runId);
        }

        foreach (var runId in desired)
        {
            if (!_joinedRunGroups.Add(runId)) continue;
            try { await _hub.InvokeAsync("JoinPipelineRunGroup", runId); }
            catch (Exception ex)
            {
                _joinedRunGroups.Remove(runId);
                logger.LogDebug(ex, "[PipelineEdit] Join run group {RunId} failed", runId);
            }
        }
    }

    private Task ScheduleReloadAsync()
    {
        lock (_reloadSync)
        {
            if (_disposed) return Task.CompletedTask;
            _reloadCts?.Cancel();
            var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
            _reloadCts = cts;
            _reloadTask = ReloadAfterDelayAsync(cts);
        }
        return Task.CompletedTask;
    }

    private async Task ReloadAfterDelayAsync(CancellationTokenSource cts)
    {
        var entered = false;
        try
        {
            await Task.Delay(250, cts.Token);
            await _reloadGate.WaitAsync(cts.Token);
            entered = true;
            if (!_disposed) await onReload();
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (Exception ex) { logger.LogWarning(ex, "[PipelineEdit] Live reload failed"); }
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
        if (pendingReload is not null) await pendingReload;

        await _reloadGate.WaitAsync();
        _reloadGate.Release();
        if (_hub is not null)
        {
            foreach (var runId in _joinedRunGroups.ToList())
            {
                try { await _hub.InvokeAsync("LeavePipelineRunGroup", runId); }
                catch (Exception ex) { logger.LogDebug(ex, "[PipelineEdit] Leave run group {RunId} failed", runId); }
            }
            _joinedRunGroups.Clear();
            await _hub.DisposeAsync();
        }

        _lifetimeCts.Dispose();
        _reloadGate.Dispose();
    }
}
