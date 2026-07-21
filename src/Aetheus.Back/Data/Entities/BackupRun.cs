// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.Enums;

namespace Aetheus.Back.Data.Entities;

/// <summary>
/// PLAN-006 4.3: one execution of a <see cref="BackupPolicy"/>. The no-fake recoverability contract lives
/// in <see cref="RestoreCheckStatus"/>: a run is <c>Unverified</c> until a restore-check actually succeeds
/// on a throwaway target; a failed check is <c>Failed</c> (red, visible), never silently "ok".
/// </summary>
public class BackupRun
{
    public int Id { get; set; }
    public int BackupPolicyId { get; set; }
    public BackupPolicy BackupPolicy { get; set; } = null!;

    public int ServerId { get; set; }

    public BackupRunStatus Status { get; set; }

    /// <summary>Path of the archive on the target server (archives stay on the host, not the control plane).</summary>
    public string? ArchivePath { get; set; }
    public long SizeBytes { get; set; }
    public string? Sha256 { get; set; }

    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public RestoreCheckStatus RestoreCheckStatus { get; set; } = RestoreCheckStatus.Unverified;
    public DateTime? RestoreCheckedAt { get; set; }
    public string? RestoreCheckMessage { get; set; }

    public string? Message { get; set; }
}
