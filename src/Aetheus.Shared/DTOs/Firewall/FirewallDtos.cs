// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;

namespace Aetheus.Shared.DTOs;

/// <summary>A single firewall (ufw) rule (ADR-024 4.2).</summary>
public sealed record FirewallRuleDto
{
    public int Number { get; init; }
    [StringLength(20)]
    public string Action { get; init; } = string.Empty;   // allow | deny
    public int? Port { get; init; }
    [StringLength(20)]
    public string Protocol { get; init; } = string.Empty;  // tcp | udp | any
    [StringLength(64)]
    public string Source { get; init; } = string.Empty;    // CIDR or "Anywhere"
    [StringLength(2000)]
    public string Raw { get; init; } = string.Empty;       // raw ufw status line, for display
}

/// <summary>Collector-&gt;heartbeat view of a server's firewall state (ADR-024 4.2).</summary>
public sealed record FirewallDataDto
{
    /// <summary>Whether ufw is present on the box (feature applicability).</summary>
    public bool Installed { get; init; }

    /// <summary>Whether ufw is active (enabled).</summary>
    public bool Active { get; init; }

    /// <summary>Whether <see cref="Active"/> and <see cref="Rules"/> came from a successful ufw status probe.</summary>
    public bool StatusKnown { get; init; }

    /// <summary>Operator-facing reason why the status could not be collected.</summary>
    [StringLength(1000)]
    public string CollectionDiagnostics { get; init; } = string.Empty;

    [MaxLength(2048)]
    public List<FirewallRuleDto> Rules { get; init; } = [];
}

/// <summary>Backend-&gt;front read view of a server's firewall (ADR-024 4.2).</summary>
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

/// <summary>Request to open/close a port (ADR-024 4.2).</summary>
public sealed record FirewallRuleRequest
{
    [Range(1, 65535)]
    public int Port { get; init; }

    /// <summary>tcp or udp.</summary>
    [Required]
    [StringLength(10)]
    [RegularExpression("^(?i)(tcp|udp)$")]
    public string Protocol { get; init; } = "tcp";

    /// <summary>Source CIDR (e.g. 10.0.0.0/8) or "any".</summary>
    [Required]
    [StringLength(64)]
    public string Source { get; init; } = "any";
}

/// <summary>Request to enable/disable the firewall (ADR-024 4.2).</summary>
public sealed record FirewallToggleRequest
{
    public bool Enabled { get; init; }
}
