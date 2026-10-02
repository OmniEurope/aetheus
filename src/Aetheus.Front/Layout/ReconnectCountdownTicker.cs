// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Layout;

/// <summary>
/// Owns the "reconnecting in N s" countdown shown by the connection-lost overlay: one cancellable
/// second-by-second run at a time, restarted whenever the tracker schedules another attempt.
///
/// Extracted from <c>MainLayout</c>, which held the token source, the loop and the cancel/dispose
/// dance inline in three separate places. The countdown is the only reason that token source
/// existed, so it belongs with the loop rather than among the layout's own state.
/// </summary>
public sealed class ReconnectCountdownTicker(TimeProvider timeProvider) : IDisposable
{
    private CancellationTokenSource? _cts;

    /// <summary>Seconds left before the next attempt; 0 when nothing is counting.</summary>
    public int Remaining { get; private set; }

    /// <summary>
    /// Restarts the countdown at <paramref name="seconds"/>, cancelling any run already in flight.
    /// <paramref name="onTick"/> is invoked after each decrement so the caller can re-render.
    /// </summary>
    public async Task RunAsync(int seconds, Func<Task> onTick)
    {
        Stop();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        Remaining = seconds;
        await onTick().ConfigureAwait(false);
        try
        {
            while (Remaining > 0 && !token.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), timeProvider, token).ConfigureAwait(false);
                Remaining--;
                await onTick().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Reconnected, or superseded by a later attempt: both are ordinary, not failures.
        }
    }

    /// <summary>Cancels any countdown in flight and clears the displayed value.</summary>
    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        Remaining = 0;
    }

    public void Dispose() => Stop();
}
