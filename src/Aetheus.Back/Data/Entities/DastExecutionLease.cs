// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Data.Entities;

/// <summary>
/// Immutable, run-owned authorization for one DAST scanner task and one exact target.
/// It is created by the scheduler, expires within 24 hours, and is required again when
/// the agent publishes the resulting report.
/// </summary>
public sealed class DastExecutionLease
{
    public int Id { get; set; }
    public string Token { get; set; } = string.Empty;
    public int PipelineRunId { get; set; }
    public int PipelineStepRunId { get; set; }
    public int EnvironmentId { get; set; }
    public string TargetHost { get; set; } = string.Empty;
    public int TargetPort { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public PipelineRun PipelineRun { get; set; } = null!;
    public PipelineStepRun PipelineStepRun { get; set; } = null!;
    public Environment Environment { get; set; } = null!;
}
