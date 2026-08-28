// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Components.Tasks;

/// <summary>
/// Compatibility window for the task-session fencing protocol introduced by agent 1.0.761.
/// The immediately preceding production agent (1.0.395) identifies its process session while
/// claiming work, but its start/log/complete payloads do not carry the fencing token yet.
/// </summary>
internal static class AgentTaskProtocolCompatibility
{
    private static readonly Version LegacyFloor = new(1, 0, 395);
    private static readonly Version FencedProtocolFloor = new(1, 0, 761);

    public static bool AllowsLegacyUnfencedRequests(string? agentVersion)
    {
        if (string.IsNullOrWhiteSpace(agentVersion))
            return false;

        var normalized = agentVersion.Trim();
        if (normalized.StartsWith('v'))
            normalized = normalized[1..];

        return Version.TryParse(normalized, out var parsed)
            && parsed >= LegacyFloor
            && parsed < FencedProtocolFloor;
    }
}
