// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;
using Aetheus.Shared.Enums;
using Aetheus.Shared.Validation;

namespace Aetheus.Shared.DTOs;

/// <summary>Read view of a backup policy (PLAN-006 4.3). Never carries the DB password.</summary>
public sealed record BackupPolicyDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public bool Enabled { get; init; }
    public int ProjectId { get; init; }
    public string? ProjectName { get; init; }
    public int ServerId { get; init; }
    public string? ServerName { get; init; }
    public BackupDbEngine DbEngine { get; init; }
    public string? DbHost { get; init; }
    public int? DbPort { get; init; }
    public string? DbName { get; init; }
    public string? DbUser { get; init; }

    /// <summary>Whether a DB password is stored (the password itself is never returned).</summary>
    public bool HasPassword { get; init; }

    [MaxLength(200)]
    [MaxItemStringLength(4096)]
    public List<string> FilePaths { get; init; } = [];
    public string ScheduleCron { get; init; } = string.Empty;
    public int RetentionCount { get; init; }
    public string? RestoreCheckCron { get; init; }
    public DateTime? LastRunAt { get; init; }
    public DateTime? LastRestoreCheckAt { get; init; }
    public DateTime CreatedAt { get; init; }
}

/// <summary>Read view of one backup run (PLAN-006 4.3).</summary>
public sealed record BackupRunDto
{
    public int Id { get; init; }
    public int BackupPolicyId { get; init; }
    public int ServerId { get; init; }
    public BackupRunStatus Status { get; init; }
    public long SizeBytes { get; init; }
    public DateTime StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public RestoreCheckStatus RestoreCheckStatus { get; init; }
    public DateTime? RestoreCheckedAt { get; init; }
    public string? RestoreCheckMessage { get; init; }
    public string? Message { get; init; }
}

/// <summary>Create a backup policy (PLAN-006 4.3). The DB password is AES-encrypted server-side.</summary>
public sealed record CreateBackupPolicyRequest
{
    [Required]
    [StringLength(120, MinimumLength = 1)]
    public string Name { get; init; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int ProjectId { get; init; }

    [Range(1, int.MaxValue)]
    public int ServerId { get; init; }

    public BackupDbEngine DbEngine { get; init; }

    [StringLength(255)]
    public string? DbHost { get; init; }

    [Range(1, 65535)]
    public int? DbPort { get; init; }

    [StringLength(120)]
    public string? DbName { get; init; }

    [StringLength(120)]
    public string? DbUser { get; init; }

    [StringLength(256)]
    public string? DbPassword { get; init; }

    [MaxLength(200)]
    [MaxItemStringLength(4096)]
    public List<string> FilePaths { get; init; } = [];

    [Required]
    [StringLength(120)]
    public string ScheduleCron { get; init; } = string.Empty;

    [Range(1, 365)]
    public int RetentionCount { get; init; } = 7;

    [StringLength(120)]
    public string? RestoreCheckCron { get; init; }
}

/// <summary>Update a backup policy. A null <see cref="DbPassword"/> leaves the stored password unchanged.</summary>
public sealed record UpdateBackupPolicyRequest
{
    [Required]
    [StringLength(120, MinimumLength = 1)]
    public string Name { get; init; } = string.Empty;

    public bool Enabled { get; init; } = true;

    public BackupDbEngine DbEngine { get; init; }

    [StringLength(255)]
    public string? DbHost { get; init; }

    [Range(1, 65535)]
    public int? DbPort { get; init; }

    [StringLength(120)]
    public string? DbName { get; init; }

    [StringLength(120)]
    public string? DbUser { get; init; }

    /// <summary>Null = keep the existing password; empty string = clear it.</summary>
    [StringLength(256)]
    public string? DbPassword { get; init; }

    [MaxLength(200)]
    [MaxItemStringLength(4096)]
    public List<string> FilePaths { get; init; } = [];

    [Required]
    [StringLength(120)]
    public string ScheduleCron { get; init; } = string.Empty;

    [Range(1, 365)]
    public int RetentionCount { get; init; } = 7;

    [StringLength(120)]
    public string? RestoreCheckCron { get; init; }
}

/// <summary>Agent-&gt;backend result of a backup execution (PLAN-006 4.3).</summary>
public sealed record BackupExecuteResultDto
{
    public bool Success { get; init; }
    public string? ArchivePath { get; init; }
    public long SizeBytes { get; init; }
    public string? Sha256 { get; init; }
    public string? Message { get; init; }
}

/// <summary>Agent-&gt;backend result of a restore-check (PLAN-006 4.3). No-fake: success is only true when a
/// real restore onto a throwaway target actually verified.</summary>
public sealed record RestoreCheckResultDto
{
    public bool Verified { get; init; }
    public string? Message { get; init; }
}
