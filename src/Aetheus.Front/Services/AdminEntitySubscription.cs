// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Services;

/// <summary>
/// RT4M: reusable wrapper around the admin SignalR hub's <c>AdminEntityChanged</c> feed. Each admin
/// list page (Users, Roles, Organizations, Plugins) carried a near-identical block: create the
/// <c>admin</c> hub, subscribe to <c>AdminEntityChanged</c>, filter by its entity name, reload on a
/// match, and dispose the connection. This collapses those four copies into one collaborator: the page
/// supplies the entity name to watch and an <paramref name="onChanged"/> callback (which marshals to
/// the renderer and reloads its grid). Callers that require eventual freshness can opt into a bounded
/// polling fallback while SignalR is unavailable. Registered transient - each page owns its own
/// instance and disposes it.
/// </summary>
public sealed class AdminEntitySubscription(
    HubConnectionFactory hubFactory,
    TimeProvider? timeProvider = null) : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private HubConnection? _hub;
    private CancellationTokenSource? _pollingCancellation;
    private Task? _pollingTask;
    private Func<Task>? _onChanged;
    private TimeSpan? _fallbackPollInterval;
    private bool _disposed;

    /// <summary>Starts listening for changes to <paramref name="entityName"/> (an <c>AdminEntities</c>
    /// constant) and invokes <paramref name="onChanged"/> on each matching broadcast.</summary>
    public async Task StartAsync(
        string entityName,
        Func<Task> onChanged,
        TimeSpan? fallbackPollInterval = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityName);
        ArgumentNullException.ThrowIfNull(onChanged);
        if (fallbackPollInterval.HasValue
            && (fallbackPollInterval.Value < TimeSpan.FromSeconds(15)
                || fallbackPollInterval.Value > TimeSpan.FromMinutes(15)))
            throw new ArgumentOutOfRangeException(
                nameof(fallbackPollInterval), "Fallback polling must be between 15 seconds and 15 minutes.");
        _onChanged = onChanged;
        _fallbackPollInterval = fallbackPollInterval;
        try
        {
            _hub = hubFactory.Create("admin");
            _hub.On<string, int, string>("AdminEntityChanged", (entity, _, _) =>
                entity == entityName ? onChanged() : Task.CompletedTask);
            _hub.Reconnected += async _ =>
            {
                await StopPollingAsync().ConfigureAwait(false);
                try { await onChanged().ConfigureAwait(false); }
                catch { /* reconnect reconciliation is best-effort */ }
            };
            _hub.Closed += _ =>
            {
                StartPolling();
                return Task.CompletedTask;
            };
            using var timeout = new CancellationTokenSource(FrontendRuntimeDefaults.SignalRStartTimeout);
            await _hub.StartAsync(timeout.Token);
        }
        catch
        {
            StartPolling();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        await StopPollingAsync().ConfigureAwait(false);
        if (_hub is not null)
        {
            await _hub.DisposeAsync().ConfigureAwait(false);
            _hub = null;
        }
    }

    private void StartPolling()
    {
        lock (_sync)
        {
            if (_disposed || !_fallbackPollInterval.HasValue || _onChanged is null
                || _pollingTask is { IsCompleted: false })
                return;
            _pollingCancellation = new CancellationTokenSource();
            var token = _pollingCancellation.Token;
            var interval = _fallbackPollInterval.Value;
            var callback = _onChanged;
            _pollingTask = PollAsync(interval, callback, token);
        }
    }

    private async Task PollAsync(TimeSpan interval, Func<Task> callback, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(interval, _timeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                try { await callback().ConfigureAwait(false); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
                catch { }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    private async Task StopPollingAsync()
    {
        Task? pollingTask;
        CancellationTokenSource? cancellation;
        lock (_sync)
        {
            pollingTask = _pollingTask;
            cancellation = _pollingCancellation;
            _pollingTask = null;
            _pollingCancellation = null;
        }
        if (cancellation is null) return;
        await cancellation.CancelAsync().ConfigureAwait(false);
        if (pollingTask is not null)
        {
            try { await pollingTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        cancellation.Dispose();
    }
}
