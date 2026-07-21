// SPDX-License-Identifier: EUPL-1.2
using Microsoft.AspNetCore.SignalR.Client;

namespace Aetheus.Front.Helpers;

/// <summary>
/// Helpers around <see cref="HubConnection"/> realtime wiring.
/// </summary>
public static class HubConnectionExtensions
{
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

        connection.Reconnected += async _ =>
        {
            try { await rejoin(); }
            catch { /* reconnect-time best effort - never fault the hub on a transient refresh failure */ }
        };
    }
}
