// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Aetheus.Back.Components.AgentUpdate;

internal sealed class AgentUpdateConfirmationService(
    IAgentUpdateRepository repository,
    IAuditService audit,
    IHubContext<ServerHub> hub,
    ILogger<AgentUpdateConfirmationService> logger) : IAgentUpdateConfirmationService
{
    public async Task ProcessHeartbeatAsync(
        int serverId,
        ServerHeartbeatDto heartbeat,
        CancellationToken ct)
    {
        var request = await repository
            .ConfirmFromHeartbeatAsync(serverId, heartbeat, ct)
            .ConfigureAwait(false);
        if (request is null) return;

        var confirmed = request.Status == AgentUpdateRequestStatus.Confirmed;
        if (!confirmed)
        {
            logger.LogError(
                "Agent update {SourceVersion} to {TargetVersion} failed on server {ServerId}: {FailureCode} - {Diagnostic}; correlation {CorrelationId}",
                request.ObservedVersion,
                request.TargetVersion,
                serverId,
                request.FailureCode,
                request.FailureDiagnostic,
                LogCorrelationIds.AgentUpdate(request.Id));
        }
        await audit.LogAsync(
            confirmed ? "AgentUpdateConfirmed" : "AgentUpdateConfirmationFailed",
            "Server",
            serverId,
            confirmed
                ? $"Agent {request.TargetVersion} confirmed by session {request.ConfirmedSessionId}"
                : $"{request.FailureCode}: {request.FailureDiagnostic}",
            ct).ConfigureAwait(false);
        await hub.Clients.Groups([HubGroups.AllServers, HubGroups.Server(serverId)])
            .SendAsync(
                confirmed ? "AgentUpdateConfirmed" : "AgentUpdateFailed",
                serverId,
                request.Id,
                ct)
            .ConfigureAwait(false);
    }
}
