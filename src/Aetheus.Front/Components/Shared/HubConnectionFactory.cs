// SPDX-License-Identifier: EUPL-1.2
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Aetheus.Front.Components.Shared;

public class HubConnectionFactory(
    IConfiguration config,
    AuthStateProvider auth,
    ILogger<AuthDelegatingHandler> handlerLogger,
    RealtimeSyncStatus? syncStatus = null)
{
    /// <summary>
    /// Where every connection built here reports its reconnect catch-up, for the "synchronising"
    /// indicator. The registered singleton in the application; a private one when none is given.
    /// </summary>
    public RealtimeSyncStatus SyncStatus { get; } = syncStatus ?? new RealtimeSyncStatus(TimeProvider.System);

    private readonly object _connectionsLock = new();
    private readonly List<WeakReference<HubConnection>> _connections = [];

    public virtual HubConnection Create(string hubPath, IRetryPolicy? retryPolicy = null)
    {
        var apiBaseUrl = config["ApiBaseUrl"] ?? LocalDevelopmentEndpoints.ApiHttpsBaseUrl;
        var builder = new HubConnectionBuilder()
            .WithUrl($"{apiBaseUrl}/hubs/{hubPath}", opts =>
            {
                opts.AccessTokenProvider = () => Task.FromResult(auth.Token);
                // S-TECH-H6RA: route the negotiate request through the same AuthDelegatingHandler as the
                // REST pipeline so it inherits the proactive token renewal (and 401-retry) logic. Without
                // this, a token that expired while only SignalR was active 401s at /negotiate and strands
                // the realtime widgets as "Disconnected" - even though the REST side would have recovered.
                // The negotiate refreshes auth.Token; the subsequent transport connect then reads the fresh
                // token from AccessTokenProvider above.
                opts.HttpMessageHandlerFactory = inner =>
                    new AuthDelegatingHandler(auth, handlerLogger) { InnerHandler = inner };
            });
        // A caller can supply a notifying retry policy to surface reconnect-attempt delays to the UI
        // (the connection-lost dialog countdown). Absent one, the default schedule (0/2/10/30s) applies.
        var connection = (retryPolicy is not null
            ? builder.WithAutomaticReconnect(retryPolicy)
            : builder.WithAutomaticReconnect())
            .Build();
        connection.ReportCatchUpTo(SyncStatus);

        lock (_connectionsLock)
        {
            _connections.RemoveAll(reference => !reference.TryGetTarget(out _));
            _connections.Add(new WeakReference<HubConnection>(connection));
        }

        return connection;
    }

    /// <summary>
    /// Raised when a new version of the application is live (the reload banner). A blue-green switch of
    /// Aetheus itself leaves the sockets already open on the PREVIOUS colour: Apache's graceful reload
    /// keeps them, and that colour stays up in reserve until Commit. SignalR has no backplane between
    /// the colours, so whatever the new colour pushes (the Confirm approval of aetheus-deploy-prod, run
    /// 2458) never reaches them, and since the old colour is still alive no reconnect ever comes. The
    /// owners that must follow the new backend restart their connection on this signal.
    /// </summary>
    public event Action? BackendReplaced;

    /// <summary>Announces that the backend behind the open connections has been replaced.</summary>
    public void AnnounceBackendReplaced() => BackendReplaced?.Invoke();

    /// <summary>
    /// Stops every live realtime connection before an explicit logout clears the access token.
    /// Connections are tracked weakly so page-scoped hubs can still be collected normally.
    /// </summary>
    public virtual async Task StopAllAsync()
    {
        HubConnection[] connections;
        lock (_connectionsLock)
        {
            connections = _connections
                .Select(reference => reference.TryGetTarget(out var connection) ? connection : null)
                .Where(connection => connection is not null)
                .Cast<HubConnection>()
                .Distinct()
                .ToArray();
            // This is a session boundary. Forget every captured connection immediately so a later
            // logout cannot revisit disposed transports from the previous authenticated user.
            _connections.Clear();
        }

        await Task.WhenAll(connections.Select(StopSafelyAsync));
    }

    private async Task StopSafelyAsync(HubConnection connection)
    {
        try
        {
            await connection.StopAsync();
        }
        catch (Exception exception)
        {
            // Logout must continue even when a best-effort realtime connection is already broken.
            handlerLogger.LogDebug(exception, "Failed to stop a realtime connection during logout");
        }
    }
}
