// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.AgentUpdate;

public interface IAgentUpdateConfirmationService
{
    Task ProcessHeartbeatAsync(int serverId, ServerHeartbeatDto heartbeat, CancellationToken ct);
}
