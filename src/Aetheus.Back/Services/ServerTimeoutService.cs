// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Servers;
using Aetheus.Back.Components.Servers.Events;
using Aetheus.Back.Hubs;
using Aetheus.Back.Services.DomainEvents;
using Aetheus.Shared.Enums;
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
        using var timer = new PeriodicTimer(_checkInterval);
        // do..while: check immediately on startup, then every interval
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
            catch (Exception ex) when (ex is Microsoft.EntityFrameworkCore.DbUpdateException or InvalidOperationException or TimeoutException)
            {
                logger.LogError(ex, "Recoverable error in ServerTimeoutService");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    internal async Task CheckServerTimeouts(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var serverRepo = scope.ServiceProvider.GetRequiredService<IServerRepository>();
        var serverHub = scope.ServiceProvider.GetRequiredService<IHubContext<ServerHub>>();
        var domainEvents = scope.ServiceProvider.GetRequiredService<IDomainEventDispatcher>();

        var staleServers = await serverRepo.GetStaleOnlineServersAsync(_heartbeatTimeout, ct).ConfigureAwait(false);

        foreach (var server in staleServers)
        {
            server.Status = ServerStatus.Offline;
            logger.LogWarning("Server {ServerName} (#{ServerId}) marked offline - no heartbeat since {LastHeartbeat}",
                server.Name, server.Id, server.LastHeartbeat);
        }

        if (staleServers.Count > 0)
        {
            await serverRepo.SaveChangesAsync(ct).ConfigureAwait(false);
            foreach (var server in staleServers)
            {
                await serverHub.Clients.Groups([HubGroups.AllServers, HubGroups.Server(server.Id)]).SendAsync("ServerOffline", server.Id, ct).ConfigureAwait(false);
                // Audit + downstream observers run on the background queue: not on the hot path.
                domainEvents.Publish(new ServerWentOfflineEvent(server.Id, server.Name, server.LastHeartbeat));
            }
        }
    }
}
