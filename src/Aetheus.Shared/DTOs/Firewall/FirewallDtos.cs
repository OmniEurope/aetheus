// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;

namespace Aetheus.Shared.DTOs;

/// <summary>A single firewall (ufw) rule (PLAN-006 4.2).</summary>
public sealed record FirewallRuleDto
{
    public int Number { get; init; }
    public string Action { get; init; } = string.Empty;   // allow | deny
    public int? Port { get; init; }
    public string Protocol { get; init; } = string.Empty;  // tcp | udp | any
    public string Source { get; init; } = string.Empty;    // CIDR or "Anywhere"
    public string Raw { get; init; } = string.Empty;       // raw ufw status line, for display
}

/// <summary>Collector-&gt;heartbeat view of a server's firewall state (PLAN-006 4.2).</summary>
public sealed record FirewallDataDto
{
    /// <summary>Whether ufw is present on the box (feature applicability).</summary>
    public bool Installed { get; init; }

    /// <summary>Whether ufw is active (enabled).</summary>
    public bool Active { get; init; }

    /// <summary>Whether <see cref="Active"/> and <see cref="Rules"/> came from a successful ufw status probe.</summary>
    public bool StatusKnown { get; init; }

    /// <summary>Operator-facing reason why the status could not be collected.</summary>
    public string CollectionDiagnostics { get; init; } = string.Empty;

    public List<FirewallRuleDto> Rules { get; init; } = [];
}

/// <summary>Backend-&gt;front read view of a server's firewall (PLAN-006 4.2).</summary>
public sealed record ServerFirewallDto
{
    public bool Installed { get; init; }
    public bool Active { get; init; }
    public bool StatusKnown { get; init; }
    public string CollectionDiagnostics { get; init; } = string.Empty;

    /// <summary>Whether the agent can MUTATE rules (firewall-manage capability present).</summary>
    public bool FirewallManagementAvailable { get; init; }

    public List<FirewallRuleDto> Rules { get; init; } = [];
}

/// <summary>Request to open/close a port (PLAN-006 4.2).</summary>
public sealed record FirewallRuleRequest
{
    [Range(1, 65535)]
    public int Port { get; init; }

    /// <summary>tcp or udp.</summary>
    [Required]
    [RegularExpression("^(?i)(tcp|udp)$")]
    public string Protocol { get; init; } = "tcp";

    /// <summary>Source CIDR (e.g. 10.0.0.0/8) or "any".</summary>
    [Required]
    [StringLength(64)]
    public string Source { get; init; } = "any";
}

/// <summary>Request to enable/disable the firewall (PLAN-006 4.2).</summary>
public sealed record FirewallToggleRequest
{
    public bool Enabled { get; init; }
}
