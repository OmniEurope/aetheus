// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.Servers;

/// <summary>
/// On-demand reachability probe for a server's agent. Agent communication is
/// poll-based (the agent pushes heartbeats / polls for tasks), so a single
/// probe reports whether a fresh heartbeat has been observed inside the
/// configured freshness window. The retry/timeout UX is driven by the caller
/// (the front-end dialog makes up to three bounded attempts). Split out from
/// <see cref="IServerService"/> for Interface Segregation.
/// </summary>
public interface IServerAgentContactService
{
    Task<ContactAgentResultDto?> ContactAgentAsync(int serverId, CancellationToken ct = default);
}
