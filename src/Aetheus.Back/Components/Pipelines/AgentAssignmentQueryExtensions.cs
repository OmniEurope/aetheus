// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Pipelines;

internal static class AgentAssignmentQueryExtensions
{
    public static IQueryable<Server> WhereAgentCan(
        this IQueryable<Server> query,
        string capability)
    {
        var token = $"\"{capability}\"";
        return query.Where(server =>
            !server.AgentUpdateReserved
            && server.AgentProtocolVersion >= AgentProtocol.MinimumSupportedVersion
            && server.AgentProtocolVersion <= AgentProtocol.MaximumSupportedVersion
            && server.AgentCapabilitiesJson != null
            && server.AgentCapabilitiesJson.Contains(token));
    }
}
