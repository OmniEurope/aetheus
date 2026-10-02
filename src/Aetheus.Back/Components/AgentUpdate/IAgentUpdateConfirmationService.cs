// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.AgentUpdate;

public interface IAgentUpdateConfirmationService
{
    /// <summary>
    /// Confirms (or fails) the server's pending agent update from this heartbeat. Returns the agent
    /// version this heartbeat confirmed, or null when it confirmed no update (none pending, still
    /// waiting, or failed), so the heartbeat can act on a completed update in the same pass.
    /// </summary>
    Task<string?> ProcessHeartbeatAsync(int serverId, ServerHeartbeatDto heartbeat, CancellationToken ct);
}
