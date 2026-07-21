// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Data.Entities;

/// <summary>
/// PLAN-006 4.2: a server's last reported firewall (ufw) state (upserted from the heartbeat). Only
/// written when ufw is present; <see cref="Installed"/> vs a populated <see cref="RulesJson"/> keeps the
/// UI honest (present-but-unreadable vs active-with-rules).
/// </summary>
public class FirewallState
{
    public int Id { get; set; }
    public int ServerId { get; set; }

    public bool Installed { get; set; }
    public bool Active { get; set; }
    public bool StatusKnown { get; set; }
    public string CollectionDiagnostics { get; set; } = string.Empty;

    /// <summary>Serialized list of firewall rules for the detail view. Null when none/unreadable.</summary>
    public string? RulesJson { get; set; }

    public DateTime LastUpdated { get; set; }

    // Navigation
    public Server Server { get; set; } = null!;
}
