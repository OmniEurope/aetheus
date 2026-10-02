// SPDX-License-Identifier: EUPL-1.2

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
    /// <summary>Structured category for failures produced by the pipeline scheduler itself.</summary>
    public string? FailureCode { get; set; }
    /// <summary>Bounded human-readable diagnostic for failures that did not create a server task.</summary>
    public string? FailureReason { get; set; }
    public string? OutputVariablesJson { get; set; }
    public int RetryCount { get; set; }
    public bool ContinueOnError { get; set; }
    public string? MatrixLeg { get; set; }
    public bool IsSystem { get; set; }
    public string? GroupName { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? SkippedCondition { get; set; }
    public string? SkippedConditionVariablesJson { get; set; }
    /// <summary>Why a step that completed Success did no work: an <c>allow_missing</c> restore that
    /// found no artifact (PLAN-007 lot 3). The status stays Success, since nothing failed; this is what
    /// keeps it from reading as a green tick for bytes that were never restored.</summary>
    public string? SkippedReason { get; set; }

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
