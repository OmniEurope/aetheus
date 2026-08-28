// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.Servers;

/// <summary>
/// Heartbeat ingestion: persistence of agent telemetry (metrics, services, module data)
/// and broadcasting of real-time updates. Called by the agent-facing controller and
/// background timeout services. Split out from <see cref="IServerService"/> for ISP.
/// </summary>
public interface IServerHeartbeatService
{
    Task ProcessHeartbeatAsync(int serverId, ServerHeartbeatDto heartbeat, CancellationToken ct = default);
}
