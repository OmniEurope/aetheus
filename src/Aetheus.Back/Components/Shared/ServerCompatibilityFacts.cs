// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Shared;

/// <summary>
/// The agent facts a compatibility verdict is computed from, read for a set of servers. Servers and
/// AgentUpdate both need them; each keeps its own repository method (neither module injects the
/// other), and both read through this one query.
/// </summary>
internal static class ServerCompatibilityFacts
{
    /// <summary>The facts of the servers in <paramref name="accessibleIds"/>, or of the whole fleet when it is null.</summary>
    public static async Task<List<ServerDto>> ReadAsync(
        IQueryable<Server> servers, List<int>? accessibleIds, CancellationToken ct)
    {
        var query = servers.AsNoTracking();
        if (accessibleIds is not null)
            query = query.Where(server => accessibleIds.Contains(server.Id));

        var facts = await query.Select(server => new
        {
            server.Id,
            server.AgentVersion,
            server.AgentProtocolVersion,
            server.AgentCapabilitiesJson,
            server.LastHeartbeat,
            server.Status,
            server.PipelineRunnerEnabled,
            server.DeploymentTargetAvailable
        }).ToListAsync(ct).ConfigureAwait(false);
        return facts.Select(server => new ServerDto
        {
            Id = server.Id,
            AgentVersion = server.AgentVersion,
            AgentProtocolVersion = server.AgentProtocolVersion,
            AgentCapabilities = ServerDataMapper.DeserializeDiagnostics(server.AgentCapabilitiesJson),
            LastHeartbeat = server.LastHeartbeat,
            Status = server.Status,
            PipelineRunnerEnabled = server.PipelineRunnerEnabled,
            DeploymentTargetAvailable = server.DeploymentTargetAvailable
        }).ToList();
    }
}
