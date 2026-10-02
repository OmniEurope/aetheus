// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Shared.Components.AgentUpdate;

public static class LogCorrelationIds
{
    public static string AgentUpdate(int requestId) => $"agent-update-{requestId}";
}
