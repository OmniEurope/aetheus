// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.Enums;

namespace Aetheus.Back.Data.Entities;

public class PipelineStepRun
{
    public int Id { get; set; }
    public int PipelineRunId { get; set; }
    public string StageName { get; set; } = string.Empty;
    public string StepName { get; set; } = string.Empty;
    public int Order { get; set; }
    public TaskExecutionStatus Status { get; set; } = TaskExecutionStatus.Pending;
    public int? ServerId { get; set; }
    public int? TaskId { get; set; }
    public int? ExitCode { get; set; }
    public string? OutputVariablesJson { get; set; }
    public int RetryCount { get; set; }
    public bool ContinueOnError { get; set; }
    public string? MatrixLeg { get; set; }
    public bool IsSystem { get; set; }
    public string? GroupName { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    /// <summary>type: trigger - the id of the child pipeline run this step launched and is waiting on.
    /// The step stays <see cref="TaskExecutionStatus.Running"/> (no agent task) until that run completes;
    /// <c>PipelineRunCompletedTriggerHandler</c> then mirrors the child's status onto this step and
    /// advances the parent stage. Null for every non-trigger step.</summary>
    public int? TriggeredRunId { get; set; }

    // Navigation
    public PipelineRun PipelineRun { get; set; } = null!;
    public Server? Server { get; set; }
    public ServerTask? Task { get; set; }
}
