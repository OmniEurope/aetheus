// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Data.Entities;

/// <summary>
/// ADR-024 4.1: a server's last reported OS-patch status (upserted from the heartbeat). Only written
/// when the server actually runs an apt package manager; <see cref="Probed"/> distinguishes a trustworthy
/// count from an UNKNOWN state (probe failed) so the UI never shows a fake "up to date".
/// </summary>
public class SecurityUpdatesState
{
    public int Id { get; set; }
    public int ServerId { get; set; }

    /// <summary>True only when the dry-run probe succeeded; false =&gt; counts are UNKNOWN.</summary>
    public bool Probed { get; set; }

    public int PendingTotal { get; set; }
    public int PendingSecurity { get; set; }

    /// <summary>Serialized (capped) list of pending updates for the detail view. Null when none/unknown.</summary>
    public string? UpdatesJson { get; set; }

    public DateTime CheckedAt { get; set; }
    public DateTime LastUpdated { get; set; }

    // Navigation
    public Server Server { get; set; } = null!;
}
