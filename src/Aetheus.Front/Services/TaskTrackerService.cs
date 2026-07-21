// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.SignalR.Client;

namespace Aetheus.Front.Services;

/// <summary>
/// Item #7 of the plan: maintains a live list of in-flight tasks (Pending / Assigned / Running)
/// visible to the current user. Drives the top-bar task tracker widget.
///
/// Single source of truth in the front for "what's running right now". Backend pushes:
///   - <c>TaskQueued</c>    : new task created - added to the list.
///   - <c>TaskStarted</c>   : task picked up by an agent - status flips to Running.
///   - <c>TaskCompleted</c> : task done (Success/Failed/Cancelled) - removed from the list.
///
/// Hub group scoping is delegated to <see cref="ServerHub.JoinAllServers"/> on the server,
/// which only adds the connection to per-server groups the user can <c>Read</c>. So this
/// service inherits RBAC for free - no event arrives for an inaccessible server.
///
/// Lifecycle: scoped (one per Blazor circuit/WASM session). Started on user login, stopped on
/// logout. Auto-refetches <c>/api/tasks/active</c> on every (re)connect so a network blip
/// cannot leave the widget out of sync.
/// </summary>
public sealed class TaskTrackerService : IAsyncDisposable
{
    private readonly ApiClient _api;
    private readonly AuthStateProvider _auth;
    private readonly HubConnectionFactory _hubFactory;
    private readonly ILogger<TaskTrackerService> _logger;

    private HubConnection? _hub;
    private readonly object _gate = new();
    private readonly Dictionary<int, ServerTaskDto> _byId = [];

    // Drives the initial-connect retry loop (see ScheduleInitialRetry) - cancelled whenever the hub
    // is torn down (StopAsync) so a superseded retry attempt never races a fresh one.
    private CancellationTokenSource? _startRetryCts;

    /// <summary>Fired whenever the visible set changes. UI subscribers re-render.</summary>
    public event Action? OnChanged;

    /// <summary>Current SignalR connection state for the global status indicator.</summary>
    public HubConnectionState ConnectionState => _hub?.State ?? HubConnectionState.Disconnected;

    /// <summary>Fired on connection state transitions (connected/reconnecting/disconnected).</summary>
    public event Action? OnConnectionStateChanged;

    /// <summary>
    /// Fired each time the automatic-reconnect policy schedules a retry, carrying the delay (seconds)
    /// before that attempt. Drives the connection-lost dialog countdown. Mirrors the Astraia wiring.
    /// </summary>
    public event Action<int>? OnReconnectAttempt;

    // The shared schedule is surfaced to the UI countdown before returning each delay to the transport.
    private static IReadOnlyList<TimeSpan> RetryDelays => FrontendRuntimeDefaults.SignalRRetryDelays;

    public TaskTrackerService(ApiClient api, AuthStateProvider auth, HubConnectionFactory hubFactory, ILogger<TaskTrackerService> logger)
    {
        _api = api;
        _auth = auth;
        _hubFactory = hubFactory;
        _logger = logger;
    }

    /// <summary>Snapshot of the currently-tracked tasks (newest first).</summary>
    public IReadOnlyList<ServerTaskDto> Tasks
    {
        get
        {
            lock (_gate)
                return _byId.Values.OrderByDescending(t => t.CreatedAt).ToList();
        }
    }

    public int Count
    {
        get
        {
            lock (_gate) return _byId.Count;
        }
    }

    /// <summary>
    /// Connects the hub and seeds the list from the active-tasks endpoint. Idempotent - calling
    /// twice (e.g. on login then on first widget render) is a no-op after the first call.
    /// </summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_hub is not null) return;
        if (!_auth.IsAuthenticated) return;

        _hub = _hubFactory.Create("servers", new NotifyingRetryPolicy(this));
        _hub.On<ServerTaskDto>("TaskQueued", task => Upsert(task));
        _hub.On<TaskStartedEvent>("TaskStarted", evt => MarkRunning(evt.TaskId, evt.StartedAt));
        _hub.On<TaskCompletedNotification>("TaskCompleted", evt => RemoveOnTerminal(evt.TaskId));

        // Refetch from /active after every (re)connect to recover any events missed while
        // disconnected. Same call seeds the initial set on first connect.
        _hub.Closed += _ => { OnConnectionStateChanged?.Invoke(); return Task.CompletedTask; };
        _hub.Reconnecting += _ => { OnConnectionStateChanged?.Invoke(); return Task.CompletedTask; };
        _hub.RejoinOnReconnect(async () =>
        {
            OnConnectionStateChanged?.Invoke();
            await SeedActiveAsync(CancellationToken.None).ConfigureAwait(false);
            try { await _hub!.InvokeAsync("JoinAllServers", CancellationToken.None).ConfigureAwait(false); }
            catch (Exception ex) { _logger.LogWarning(ex, "[TaskTracker] JoinAllServers on reconnect failed"); }
        });

        if (await ConnectOnceAsync(ct).ConfigureAwait(false))
            return;

        // The retry policy above only drives SignalR's *automatic* reconnect, which only arms after
        // a FIRST successful connect - a backend that is down at initial load would otherwise leave
        // the hub Disconnected forever with no retry, stranding real-time until a manual page reload.
        // Keep retrying the same (never-connected) hub on the same backoff schedule until it succeeds,
        // notifying OnReconnectAttempt so the connection-lost dialog's countdown can arm too.
        ScheduleInitialRetry();
    }

    private async Task<bool> ConnectOnceAsync(CancellationToken ct)
    {
        if (_hub is null) return false;
        try
        {
            await _hub.StartAsync(ct).ConfigureAwait(false);
            OnConnectionStateChanged?.Invoke();
            await _hub.InvokeAsync("JoinAllServers", ct).ConfigureAwait(false);
            await SeedActiveAsync(ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            // The tracker is a UI affordance - don't fail the whole login if the hub is sour.
            _logger.LogWarning(ex, "[TaskTracker] StartAsync failed; widget will stay empty");
            OnConnectionStateChanged?.Invoke();
            return false;
        }
    }

    private void ScheduleInitialRetry()
    {
        _startRetryCts?.Cancel();
        _startRetryCts?.Dispose();
        _startRetryCts = new CancellationTokenSource();
        _ = InitialRetryLoopAsync(_startRetryCts.Token);
    }

    private async Task InitialRetryLoopAsync(CancellationToken token)
    {
        var attempt = 0;
        while (!token.IsCancellationRequested && _hub is not null && _auth.IsAuthenticated)
        {
            var delay = RetryDelays[Math.Min(attempt, RetryDelays.Count - 1)];
            OnReconnectAttempt?.Invoke((int)delay.TotalSeconds);
            try { await Task.Delay(delay, token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
            if (token.IsCancellationRequested || _hub is null) return;

            if (await ConnectOnceAsync(token).ConfigureAwait(false))
            {
                // Connected: retire the retry CTS instead of leaking it until the next Stop/Dispose.
                _startRetryCts?.Dispose();
                _startRetryCts = null;
                return;
            }
            attempt++;
        }
    }

    private async Task SeedActiveAsync(CancellationToken ct)
    {
        try
        {
            var active = await _api.GetActiveTasksAsync(ct).ConfigureAwait(false);
            lock (_gate)
            {
                _byId.Clear();
                foreach (var t in active)
                    _byId[t.Id] = t;
            }
            OnChanged?.Invoke();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[TaskTracker] SeedActiveAsync failed");
        }
    }

    private void Upsert(ServerTaskDto task)
    {
        lock (_gate)
            _byId[task.Id] = task;
        OnChanged?.Invoke();
    }

    private void MarkRunning(int taskId, DateTime? startedAt)
    {
        lock (_gate)
        {
            if (!_byId.TryGetValue(taskId, out var existing)) return;
            _byId[taskId] = existing with
            {
                Status = TaskExecutionStatus.Running,
                StartedAt = startedAt ?? existing.StartedAt
            };
        }
        OnChanged?.Invoke();
    }

    private void RemoveOnTerminal(int taskId)
    {
        bool removed;
        lock (_gate)
            removed = _byId.Remove(taskId);
        if (removed) OnChanged?.Invoke();
    }

    /// <summary>
    /// Forces a fresh connection attempt - used by the connection-lost dialog's manual "reconnect"
    /// button. Tears down the (dead) hub and starts a new one; the state-change events fire through
    /// the normal path so the dialog closes on success or re-arms its countdown on failure.
    /// </summary>
    public async Task ReconnectAsync(CancellationToken ct = default)
    {
        // Keep the last-known task list visible across the reconnect attempt instead of blanking the
        // widget immediately - ConnectOnceAsync's SeedActiveAsync repopulates it once the new hub
        // connects; if the attempt fails, a stale-but-non-empty list beats an empty one.
        await StopAsync(clearTasks: false).ConfigureAwait(false);
        await StartAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Tears down the hub. <paramref name="clearTasks"/> defaults to true for the logout/dispose path;
    /// pass false ahead of a reconnect attempt so the visible task list survives instead of flashing
    /// empty for the duration of the attempt.
    /// </summary>
    public async Task StopAsync(bool clearTasks = true)
    {
        if (_hub is null) return;
        _startRetryCts?.Cancel();
        _startRetryCts?.Dispose();
        _startRetryCts = null;
        try { await _hub.DisposeAsync().ConfigureAwait(false); }
        catch (Exception ex) { _logger.LogDebug(ex, "[TaskTracker] hub dispose error"); }
        _hub = null;
        if (clearTasks)
        {
            lock (_gate) _byId.Clear();
            OnChanged?.Invoke();
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    /// <summary>Shape of the TaskStarted SignalR payload - see TaskService.StartTaskAsync.</summary>
    private sealed record TaskStartedEvent(int TaskId, int ServerId, DateTime? StartedAt);

    /// <summary>
    /// Publishes each reconnect attempt's delay via <see cref="OnReconnectAttempt"/> so the UI can show
    /// a live "reconnect in N" countdown. Retries indefinitely, clamping to the last (30s) delay, so a
    /// backend that is down for a while is auto-rejoined the moment it returns - the connection-lost
    /// dialog then closes on its own (matches the Astraia behaviour).
    /// </summary>
    private sealed class NotifyingRetryPolicy(TaskTrackerService owner) : IRetryPolicy
    {
        public TimeSpan? NextRetryDelay(RetryContext retryContext)
        {
            var index = (int)Math.Min(retryContext.PreviousRetryCount, RetryDelays.Count - 1);
            var delay = RetryDelays[index];
            owner.OnReconnectAttempt?.Invoke((int)delay.TotalSeconds);
            return delay;
        }
    }
}
