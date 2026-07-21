// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.Constants;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Aetheus.Front.Services;

public class HubConnectionFactory(IConfiguration config, AuthStateProvider auth, ILogger<AuthDelegatingHandler> handlerLogger)
{
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

        lock (_connectionsLock)
        {
            _connections.RemoveAll(reference => !reference.TryGetTarget(out _));
            _connections.Add(new WeakReference<HubConnection>(connection));
        }

        return connection;
    }

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
            _connections.RemoveAll(reference => !reference.TryGetTarget(out _));
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
