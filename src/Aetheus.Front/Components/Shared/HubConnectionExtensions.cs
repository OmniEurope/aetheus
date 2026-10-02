// SPDX-License-Identifier: EUPL-1.2
using System.Runtime.CompilerServices;

namespace Aetheus.Front.Components.Shared;

/// <summary>
/// Helpers around <see cref="HubConnection"/> realtime wiring.
/// </summary>
public static class HubConnectionExtensions
{
    // The catch-up span of each connection the factory built, so RejoinOnReconnect can report when its
    // refetch is done without changing the signature every realtime site already calls. Weak keys: a
    // page-scoped connection is collected with its page.
    private static readonly ConditionalWeakTable<HubConnection, ReconnectCatchUp> CatchUps = new();

    /// <summary>
    /// Reports every reconnect of <paramref name="connection"/> to <paramref name="status"/>, from the
    /// moment it starts reconnecting until the re-join handlers registered afterwards have finished.
    /// Called by <see cref="HubConnectionFactory.Create"/> before any caller can register a handler, so
    /// its own Reconnected handler always runs first.
    /// </summary>
    internal static void ReportCatchUpTo(this HubConnection connection, RealtimeSyncStatus status)
    {
        var catchUp = new ReconnectCatchUp(status);
        CatchUps.AddOrUpdate(connection, catchUp);
        connection.Reconnecting += _ => { catchUp.Begin(); return Task.CompletedTask; };
        connection.Reconnected += _ => { catchUp.TransportRestored(); return Task.CompletedTask; };
        connection.Closed += _ => { catchUp.End(); return Task.CompletedTask; };
    }

    /// <summary>
    /// Registers a <see cref="HubConnection.Reconnected"/> handler that runs <paramref name="rejoin"/>.
    /// SignalR group membership is per-connection and is lost when the transport silently reconnects,
    /// so every page that joined a group must re-join (and usually refresh) once the socket is back.
    /// Centralizing the registration keeps that contract consistent across the ~20 realtime sites and
    /// gives a single stable signature to wire from page code-behind.
    /// </summary>
    /// <param name="connection">The hub connection to wire.</param>
    /// <param name="rejoin">
    /// Work to run after each reconnect - typically re-join the relevant group(s) and refresh state.
    /// Exceptions are swallowed so a transient post-reconnect failure cannot fault the connection.
    /// </param>
    public static void RejoinOnReconnect(this HubConnection connection, Func<Task> rejoin)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(rejoin);

        // The refetch is part of the catch-up the "synchronising" indicator waits for.
        CatchUps.TryGetValue(connection, out var catchUp);
        catchUp?.AddRejoinHandler();
        connection.Reconnected += async _ =>
        {
            try { await rejoin(); }
            catch { /* reconnect-time best effort - never fault the hub on a transient refresh failure */ }
            finally { catchUp?.RejoinFinished(); }
        };
    }

    /// <summary>
    /// Re-opens <paramref name="connection"/> so a fresh negotiate lands on the backend the proxy
    /// routes to now (see <see cref="HubConnectionFactory.BackendReplaced"/>), then runs
    /// <paramref name="rejoin"/> once. The same connection object is stopped and started again, so no
    /// second connection can pile up. A connection already connecting or reconnecting is left alone:
    /// its attempt negotiates afresh anyway and its own Reconnected handler re-joins. Callers serialize
    /// calls themselves. Returns false when nothing was restarted.
    /// </summary>
    public static async Task<bool> RestartOnCurrentBackendAsync(this HubConnection connection, Func<Task> rejoin)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(rejoin);

        if (connection.State is HubConnectionState.Connecting or HubConnectionState.Reconnecting)
            return false;
        if (connection.State == HubConnectionState.Connected)
            await connection.StopAsync();
        await connection.StartAsync();
        await rejoin();
        return true;
    }
}
