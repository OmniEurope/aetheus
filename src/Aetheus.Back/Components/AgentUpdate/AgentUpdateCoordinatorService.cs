// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Tasks;
using Aetheus.Back.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Aetheus.Back.Components.AgentUpdate;

internal sealed class AgentUpdateCoordinatorService(
    IServiceScopeFactory scopeFactory,
    IHubContext<ServerHub> hub,
    TimeProvider timeProvider,
    ILogger<AgentUpdateCoordinatorService> logger,
    IPostgresLeaderLease? leaderLease = null) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        leaderLease is null
            ? RunLeaderLoopAsync(stoppingToken)
            : leaderLease.RunAsLeaderAsync(
                "aetheus:agent-update-coordinator",
                RunLeaderLoopAsync,
                stoppingToken);

    private async Task RunLeaderLoopAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await CoordinateAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Agent update coordinator iteration failed");
            }
        }
    }

    internal async Task CoordinateAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAgentUpdateRepository>();
        var taskService = scope.ServiceProvider.GetRequiredService<ITaskService>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditService>();

        var waiting = await repository.GetWaitingRequestIdsAsync(ct).ConfigureAwait(false);
        foreach (var requestId in waiting)
        {
            var (request, task, _) = await repository.TryQueueAsync(requestId, ct).ConfigureAwait(false);
            if (task is null) continue;

            await taskService.NotifyTaskQueuedAsync(task, request.Server.Name, ct).ConfigureAwait(false);
            await hub.Clients.Groups([HubGroups.AllServers, HubGroups.Server(request.ServerId)])
                .SendAsync("AgentUpdateQueued", request.ServerId, ct)
                .ConfigureAwait(false);
            await audit.LogAsync(
                "AgentUpdateQueuedAfterDrain",
                "Server",
                request.ServerId,
                $"Agent update task {task.Id} queued after active work completed",
                ct).ConfigureAwait(false);
        }

        var expired = await repository.FailExpiredAsync(ct).ConfigureAwait(false);
        foreach (var request in expired)
        {
            logger.LogError(
                "Agent update {SourceVersion} to {TargetVersion} failed on server {ServerId}: {FailureCode} - {Diagnostic}; correlation {CorrelationId}",
                request.ObservedVersion,
                request.TargetVersion,
                request.ServerId,
                request.FailureCode,
                request.FailureDiagnostic,
                LogCorrelationIds.AgentUpdate(request.Id));
            await hub.Clients.Groups([HubGroups.AllServers, HubGroups.Server(request.ServerId)])
                .SendAsync("AgentUpdateFailed", request.ServerId, request.Id, ct)
                .ConfigureAwait(false);
            await audit.LogAsync(
                "AgentUpdateConfirmationFailed",
                "Server",
                request.ServerId,
                $"{request.FailureCode}: {request.FailureDiagnostic}",
                ct).ConfigureAwait(false);
        }
    }
}
