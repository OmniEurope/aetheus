// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.Enums;

namespace Aetheus.Back.Data.Entities;

public class PipelineApproval
{
    public int Id { get; set; }
    public int PipelineRunId { get; set; }
    public string StageName { get; set; } = string.Empty;
    public int EnvironmentId { get; set; }
    public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;
    public DateTime RequestedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public int? ResolvedByUserId { get; set; }
    public string? Comments { get; set; }

    // Navigation
    public PipelineRun PipelineRun { get; set; } = null!;
    public Environment Environment { get; set; } = null!;
    public User? ResolvedByUser { get; set; }
}
