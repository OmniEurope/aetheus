// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.Enums;

namespace Aetheus.Back.Data.Entities;

/// <summary>Durable correlation between a manual rollback request and its deployment pipeline run.
/// It prevents a queued request from pretending that the application already moved back to N-1.</summary>
public class ReleaseRollback
{
    public int Id { get; set; }
    public int SourceReleaseId { get; set; }
    public int TargetReleaseId { get; set; }
    public int PipelineId { get; set; }
    public int? PipelineRunId { get; set; }
    public int? BackupRunId { get; set; }
    public bool RestoreDatabase { get; set; }
    public RollbackStatus Status { get; set; } = RollbackStatus.Pending;
    public DateTime RequestedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? FailureReason { get; set; }

    public Release SourceRelease { get; set; } = null!;
    public Release TargetRelease { get; set; } = null!;
    public Pipeline Pipeline { get; set; } = null!;
    public PipelineRun? PipelineRun { get; set; }
}
