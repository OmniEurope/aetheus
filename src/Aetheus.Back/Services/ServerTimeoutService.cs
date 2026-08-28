// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Servers;
using Aetheus.Back.Components.Servers.Events;
using Aetheus.Back.Hubs;
using Aetheus.Back.Services.DomainEvents;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace Aetheus.Back.Services;

public sealed class ServerTimeoutService(
    IServiceScopeFactory scopeFactory,
    ILogger<ServerTimeoutService> logger,
    IOptions<BackgroundServicesOptions> options,
    TimeProvider timeProvider) : BackgroundService
{
    private readonly TimeSpan _checkInterval = options.Value.ServerCheckInterval;
    private readonly TimeSpan _heartbeatTimeout = options.Value.ServerHeartbeatTimeout;
    private readonly TimeProvider _timeProvider = timeProvider;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // A backend restart makes every persisted heartbeat look stale until the agents complete
        // their next scheduled POST. Give them one full freshness window before the first sweep;
        // otherwise a normal deploy produces a fleet-wide Offline transition and false warning
        // toasts seconds before the agents reconnect.
        await WaitForStartupGraceAsync(stoppingToken).ConfigureAwait(false);

        using var timer = new PeriodicTimer(_checkInterval);
        do
        {
            try
            {
                await CheckServerTimeouts(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                logger.LogCritical(
                    ex,
                    "Server timeout sweep failed; the next sweep will retry without stopping the backend");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    internal Task WaitForStartupGraceAsync(CancellationToken ct) =>
        Task.Delay(_heartbeatTimeout, _timeProvider, ct);

    internal async Task CheckServerTimeouts(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var serverRepo = scope.ServiceProvider.GetRequiredService<IServerRepository>();
        var serverHub = scope.ServiceProvider.GetRequiredService<IHubContext<ServerHub>>();
        var domainEvents = scope.ServiceProvider.GetRequiredService<IDomainEventDispatcher>();

        var staleServers = await serverRepo.GetStaleOnlineServersAsync(_heartbeatTimeout, ct).ConfigureAwait(false);

        foreach (var server in staleServers)
        {
            // A heartbeat may have landed after the stale candidate query. Claim the transition
            // with the observed timestamp so that fresh presence can never be overwritten by this
            // sweep, and only broadcast when the conditional update actually won.
            if (!await serverRepo.TryMarkOfflineIfStaleAsync(
                    server.Id, server.LastHeartbeat, _heartbeatTimeout, ct).ConfigureAwait(false))
                continue;

            logger.LogWarning("Server {ServerName} (#{ServerId}) marked offline - no heartbeat since {LastHeartbeat}",
                server.Name, server.Id, server.LastHeartbeat);
            await serverHub.Clients.Groups([HubGroups.AllServers, HubGroups.Server(server.Id)])
                .SendAsync("ServerOffline", server.Id, ct).ConfigureAwait(false);
            // Audit + downstream observers run on the background queue: not on the hot path.
            domainEvents.Publish(new ServerWentOfflineEvent(server.Id, server.Name, server.LastHeartbeat));
        }
    }
}
