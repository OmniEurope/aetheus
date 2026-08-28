// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;

namespace Aetheus.Shared.DTOs;

/// <summary>A single pending OS package update (ADR-024 4.1).</summary>
public sealed record PendingUpdateDto
{
    [StringLength(255)]
    public string Package { get; init; } = string.Empty;
    [StringLength(255)]
    public string CurrentVersion { get; init; } = string.Empty;
    [StringLength(255)]
    public string CandidateVersion { get; init; } = string.Empty;
    public bool IsSecurity { get; init; }
}

/// <summary>
/// Collector-&gt;heartbeat view of a server's pending OS updates (ADR-024 4.1). No-fake contract:
/// <see cref="PackageManagerPresent"/> false = not an apt box (feature N/A); present but
/// <see cref="ProbeSucceeded"/> false = the dry-run failed, so counts are UNKNOWN, never reported as
/// "0 = up to date". Only a successful probe yields trustworthy counts.
/// </summary>
public sealed record SecurityUpdatesDataDto
{
    public bool PackageManagerPresent { get; init; }
    public bool ProbeSucceeded { get; init; }
    public int PendingTotal { get; init; }
    public int PendingSecurity { get; init; }
    public List<PendingUpdateDto> Updates { get; init; } = [];
}

/// <summary>Backend-&gt;front read view of a server's patch status (ADR-024 4.1).</summary>
public sealed record ServerSecurityUpdatesDto
{
    /// <summary>False when the server has never reported a successful probe (state = UNKNOWN).</summary>
    public bool Known { get; init; }

    /// <summary>False when the server is not an apt-based box (feature not applicable).</summary>
    public bool PackageManagerPresent { get; init; }

    /// <summary>Whether the agent can APPLY updates (patch-manage capability present).</summary>
    public bool PatchManagementAvailable { get; init; }

    public int PendingTotal { get; init; }
    public int PendingSecurity { get; init; }
    public DateTime? CheckedAt { get; init; }
    public List<PendingUpdateDto> Updates { get; init; } = [];
}

/// <summary>Request to run a system package upgrade: dry-run preview or consented apply (ADR-024 4.1).</summary>
public sealed record SystemUpgradeRequest
{
    public bool DryRun { get; init; }
}
