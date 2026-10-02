// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Front.Components.Shared;

/// <summary>
/// Recette R-333: whether a page is loading after a navigation, for the progress bar under the top bar.
/// A navigation opens a load window; every API request sent while it is open is counted, and the window
/// closes once they have all answered (after a short settle, so chained requests stay one load). A
/// navigation that sends nothing closes after <see cref="Grace"/>, and a load that never settles closes
/// after <see cref="Cap"/>. Requests sent outside a window (background refreshes, polls) are ignored,
/// so the bar never flickers on its own.
/// </summary>
public sealed class PageLoadActivity
{
    internal static readonly TimeSpan Grace = TimeSpan.FromMilliseconds(400);
    internal static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(150);
    internal static readonly TimeSpan Cap = TimeSpan.FromSeconds(20);

    private readonly Lock _gate = new();
    private int _generation;
    private int _pending;
    private bool _active;

    public bool IsActive
    {
        get { lock (_gate) return _active; }
    }

    public event Action? Changed;

    /// <summary>Opens a load window; a previous one still open is replaced.</summary>
    public void Begin()
    {
        int generation;
        lock (_gate)
        {
            generation = ++_generation;
            _pending = 0;
            _active = true;
        }
        Changed?.Invoke();
        _ = CloseAfterAsync(generation, Grace, requireIdle: true);
        _ = CloseAfterAsync(generation, Cap, requireIdle: false);
    }

    /// <summary>Counts a request sent while a window is open; returns its window, or null when none is.</summary>
    internal int? TrackStart()
    {
        lock (_gate)
        {
            if (!_active) return null;
            _pending++;
            return _generation;
        }
    }

    internal void TrackEnd(int generation)
    {
        lock (_gate)
        {
            if (generation != _generation) return;
            _pending--;
            if (_pending > 0) return;
        }
        _ = CloseAfterAsync(generation, Settle, requireIdle: true);
    }

    private async Task CloseAfterAsync(int generation, TimeSpan delay, bool requireIdle)
    {
        await Task.Delay(delay).ConfigureAwait(false);
        lock (_gate)
        {
            if (generation != _generation || !_active) return;
            if (requireIdle && _pending > 0) return;
            _active = false;
        }
        Changed?.Invoke();
    }
}

/// <summary>Recette R-333: reports each API request to <see cref="PageLoadActivity"/>.</summary>
public sealed class PageLoadActivityHandler(PageLoadActivity activity) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var generation = activity.TrackStart();
        try
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (generation is { } window) activity.TrackEnd(window);
        }
    }
}
