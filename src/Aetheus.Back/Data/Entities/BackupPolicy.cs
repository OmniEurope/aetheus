// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.Enums;

namespace Aetheus.Back.Data.Entities;

/// <summary>
/// PLAN-006 4.3: an orchestrated backup policy for a managed app. Project-owned (RBAC scope) with a
/// target server (the app's host). Backs up a DB (pg_dump/mysqldump) plus optional file paths on a cron
/// schedule; a separate restore-check cadence verifies recoverability. No secrets in logs (the DB
/// password is AES-encrypted at rest and passed to the agent only via a protected env var).
/// </summary>
public class BackupPolicy
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;

    /// <summary>Owning project (RBAC scope). Authorization is checked against this project.</summary>
    public int ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    /// <summary>Target server (where the app + DB run and the backup task executes).</summary>
    public int ServerId { get; set; }
    public Server Server { get; set; } = null!;

    // --- What to back up ---
    public BackupDbEngine DbEngine { get; set; }
    public string? DbHost { get; set; }
    public int? DbPort { get; set; }
    public string? DbName { get; set; }
    public string? DbUser { get; set; }

    /// <summary>AES-encrypted DB password (nullable: local peer/ident auth needs none). Never logged.</summary>
    public string? DbPasswordEncrypted { get; set; }

    /// <summary>Optional JSON array of file/dir paths to archive alongside the DB dump.</summary>
    public string? FilePathsJson { get; set; }

    // --- Schedule + retention + verification ---
    public string ScheduleCron { get; set; } = string.Empty;
    public int RetentionCount { get; set; } = 7;

    /// <summary>Cron cadence for the restore-check. Null = never auto-verify (backups stay unverified).</summary>
    public string? RestoreCheckCron { get; set; }

    public DateTime? LastRunAt { get; set; }
    public DateTime? LastRestoreCheckAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation
    public List<BackupRun> Runs { get; set; } = [];
}
