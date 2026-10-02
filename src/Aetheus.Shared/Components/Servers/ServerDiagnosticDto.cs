// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Shared.Components.Servers;

/// <summary>
/// One-shot diagnostic for "why is this server offline?" - designed so the UI
/// can render a single explanation chip without making the operator chase
/// <c>journalctl</c>. All times are UTC on the wire; the global front converter
/// flips them to local for display.
/// </summary>
public record ServerDiagnosticDto
{
    /// <summary>Last observed heartbeat UTC time. Null = never reported.</summary>
    public DateTime? LastHeartbeatUtc { get; init; }

    /// <summary>Convenience - seconds since the last heartbeat, capped at 0.</summary>
    public double? SecondsSinceLastHeartbeat { get; init; }

    /// <summary>True when at least one active, non-expired, non-revoked agent token exists.</summary>
    public bool TokenValid { get; init; }

    /// <summary>UTC expiry of the most-recently-issued non-revoked agent token. Null when no such token exists.</summary>
    public DateTime? TokenExpiresAt { get; init; }

    /// <summary>Days remaining before the latest active token expires. Negative when expired. Null when no token.</summary>
    public double? TokenDaysRemaining { get; init; }

    /// <summary>Agent-reported version string (semver-ish). Empty when never reported.</summary>
    public string AgentVersion { get; init; } = string.Empty;

    /// <summary>Backend assembly version. Always populated.</summary>
    public string BackendVersion { get; init; } = string.Empty;

    /// <summary>
    /// True when agent and backend are deemed compatible (currently always true unless
    /// the agent has never reported). Reserved for future hard breaks.
    /// </summary>
    public bool? VersionsCompatible { get; init; }
    public AgentCompatibilityDto? AgentCompatibility { get; init; }

    /// <summary>
    /// Single short human-readable line the UI can render front-and-center -
    /// e.g. "Token expired 3 d ago" or "No heartbeat for 12 m". Never null.
    /// </summary>
    public string Summary { get; init; } = string.Empty;
}
