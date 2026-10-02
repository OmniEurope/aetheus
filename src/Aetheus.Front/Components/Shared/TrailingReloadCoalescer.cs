// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Front.Components.Shared;

/// <summary>
/// #12 / #7: coalesces a burst of trailing-edge reload requests into a single deferred action. The first
/// request schedules the reload after a debounce window and every request inside that window is absorbed,
/// so a flood of SignalR pushes (a fleet's clustered heartbeats, org-wide pipeline run events that a
/// project/server-scoped list can't filter on) can't thrash a LoadData grid. Extracted from the
/// hand-rolled "_…ReloadScheduled" flags so the reload-decision is unit-testable: the delay is injectable,
/// letting a test drive the window without real time. Not thread-safe across threads - Blazor dispatches
/// component handlers on a single synchronization context, which is the only caller.
/// </summary>
public sealed class TrailingReloadCoalescer(int windowMs, Func<int, CancellationToken, Task>? delay = null)
{
    private readonly Func<int, CancellationToken, Task> _delay = delay ?? ((ms, ct) => Task.Delay(ms, ct));
    private bool _scheduled;
    private bool _reloading;
    private bool _reloadRequestedDuringReload;

    /// <summary>True while a reload is pending for the current window (further requests are absorbed).</summary>
    public bool IsScheduled => _scheduled;

    /// <summary>
    /// Schedule <paramref name="reload"/> on the trailing edge of the debounce window. Returns immediately
    /// (the request is absorbed) when a reload is already pending. The trailing edge means the reload
    /// reflects the latest state of the burst, not a mid-burst snapshot.
    /// </summary>
    public async Task RequestAsync(Func<Task> reload, CancellationToken ct = default)
    {
        if (_scheduled)
        {
            if (_reloading) _reloadRequestedDuringReload = true;
            return;
        }
        _scheduled = true;
        try
        {
            await _delay(windowMs, ct).ConfigureAwait(false);
            do
            {
                _reloadRequestedDuringReload = false;
                _reloading = true;
                try
                {
                    await reload().ConfigureAwait(false);
                }
                finally
                {
                    _reloading = false;
                }
            } while (_reloadRequestedDuringReload);
        }
        finally
        {
            _scheduled = false;
        }
    }

    /// <summary>
    /// The realtime feeds' variant of <see cref="RequestAsync"/>: no reload when none is set yet, and a
    /// failed or cancelled reload is swallowed, since the view reports its own load failures and the
    /// next push retries.
    /// </summary>
    public async Task RequestBestEffortAsync(Func<Task>? reload, CancellationToken ct)
    {
        if (reload is null) return;
        try
        {
            await RequestAsync(reload, ct).ConfigureAwait(false);
        }
        catch
        {
            // The view reports its own load failures; the next push retries the reload.
        }
    }
}
