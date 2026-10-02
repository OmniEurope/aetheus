// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Shared;

/// <summary>
/// One hub connection's share of <see cref="RealtimeSyncStatus"/>: the span from the moment the
/// connection starts reconnecting to the moment every re-join handler registered through
/// <see cref="HubConnectionExtensions.RejoinOnReconnect"/> has finished its refetch. The transport being
/// back is not the end of it: the page is current only once what was missed has been fetched again.
/// A connection that closes instead ends its span, since nothing is catching up any more.
/// </summary>
internal sealed class ReconnectCatchUp(RealtimeSyncStatus status)
{
    private readonly object _gate = new();
    private IDisposable? _token;
    private int _rejoinHandlers;
    private int _outstanding;

    /// <summary>A re-join handler was registered on the connection.</summary>
    public void AddRejoinHandler()
    {
        lock (_gate) _rejoinHandlers++;
    }

    /// <summary>The connection started reconnecting.</summary>
    public void Begin()
    {
        lock (_gate)
        {
            _outstanding = 0;
            _token ??= status.BeginCatchUp();
        }
    }

    /// <summary>
    /// The transport is back. Runs before the re-join handlers (it is subscribed first), so it only
    /// arms the count they then bring down; a connection without any ends here.
    /// </summary>
    public void TransportRestored()
    {
        bool done;
        lock (_gate)
        {
            _outstanding = _rejoinHandlers;
            done = _outstanding == 0;
        }
        if (done) End();
    }

    /// <summary>One re-join handler finished, successfully or not.</summary>
    public void RejoinFinished()
    {
        bool done;
        lock (_gate)
        {
            if (_outstanding == 0) return;
            done = --_outstanding == 0;
        }
        if (done) End();
    }

    /// <summary>Ends the span, whatever is still outstanding.</summary>
    public void End()
    {
        IDisposable? token;
        lock (_gate)
        {
            token = _token;
            _token = null;
            _outstanding = 0;
        }
        token?.Dispose();
    }
}
