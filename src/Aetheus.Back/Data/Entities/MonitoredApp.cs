// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.Enums;

namespace Aetheus.Back.Data.Entities;

/// <summary>
/// An application whose availability Aetheus monitors (PLAN-001 phase 1). RBAC and org-scope are
/// inherited from the parent <see cref="Project"/>; there is no OrganizationId of its own.
/// </summary>
public class MonitoredApp
{
    public int Id { get; set; }

    public int ProjectId { get; set; }

    /// <summary>Optional environment the app belongs to (informative).</summary>
    public int? EnvironmentId { get; set; }

    /// <summary>Server hosting the app; null = off-fleet app probed directly by the backend (public URL only).</summary>
    public int? ServerId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? ProbeUrl { get; set; }

    public int ProbeIntervalSeconds { get; set; } = 60;
    public int ProbeTimeoutSeconds { get; set; } = 10;
    public int ExpectedStatusCode { get; set; } = 200;
    public int FailureThreshold { get; set; } = 3;
    public int RecoveryThreshold { get; set; } = 2;
    public bool Enabled { get; set; } = true;

    public AppHealthStatus CurrentStatus { get; set; } = AppHealthStatus.Unknown;
    public DateTime? LastStatusChangeAt { get; set; }
    public DateTime? LastCheckedAt { get; set; }
    public int? LastResponseTimeMs { get; set; }

    /// <summary>Anti-flapping counters driving the state machine.</summary>
    public int ConsecutiveFailures { get; set; }
    public int ConsecutiveSuccesses { get; set; }

    /// <summary>
    /// SHA-256 hash of the OTLP ingestion key (PLAN-001 phase 2). The plaintext is shown once at
    /// generation and never stored, mirroring RegistrationToken (leçon C-2). Null = no key issued.
    /// </summary>
    public string? IngestKeyHash { get; set; }
    public DateTime? IngestKeyCreatedAt { get; set; }

    /// <summary>Data points rejected by the ingestion cardinality/rate caps (surfaced in the UI - no silent loss).</summary>
    public long IngestDroppedCount { get; set; }
    public DateTime? LastIngestAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation
    public Project Project { get; set; } = null!;
    public Environment? Environment { get; set; }
    public Server? Server { get; set; }
}
