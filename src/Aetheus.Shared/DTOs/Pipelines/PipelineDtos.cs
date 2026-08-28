// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Aetheus.Shared.Enums;
using Aetheus.Shared.Validation;

namespace Aetheus.Shared.DTOs;

public sealed record PipelineDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string YamlDefinition { get; init; } = string.Empty;
    public PipelineTriggerType TriggerType { get; init; }
    public int? ProjectId { get; init; }
    public string? ProjectName { get; init; }
    public int? SourceRepositoryId { get; init; }
    public string? SourceBranch { get; init; }
    public int? EnvironmentId { get; init; }
    public string? EnvironmentName { get; init; }
    public int? ProjectServerId { get; init; }
    public string? ProjectServerName { get; init; }
    public PipelineStatus? LastRunStatus { get; init; }
    public DateTime? LastRunAt { get; init; }
    public List<PipelineRunSummaryDto> RecentRuns { get; init; } = [];
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

/// <summary>Authoritative Git location of a project-owned pipeline definition. The pipeline YAML is
/// committed under <c>.pipeline/</c> on its selected source branch; the database is a mirror used
/// for querying and access control.</summary>
public sealed record PipelineSourceDto
{
    public int RepositoryId { get; init; }
    public string CloneUrl { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public string Branch { get; init; } = string.Empty;
    public string? CommitHash { get; init; }
}

/// <summary>Read-only dependency view of project pipelines. A parent references at least one
/// other pipeline through <c>on_success</c> or a trigger step; a leaf references none.</summary>
public sealed record PipelineDependencyGroupsDto
{
    public List<PipelineDependencyDto> Parents { get; init; } = [];
    public List<PipelineDependencyDto> Leaves { get; init; } = [];
}

public sealed record PipelineDependencyDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public int? ProjectId { get; init; }
    public string? ProjectName { get; init; }
    public PipelineTriggerType TriggerType { get; init; }
    /// <summary>The five most recent runs, newest first.</summary>
    public List<PipelineRunSummaryDto> RecentRuns { get; init; } = [];
    /// <summary>Pipelines launched by this pipeline, in YAML execution order.</summary>
    public List<PipelineDependencyReferenceDto> References { get; init; } = [];
    /// <summary>Pipelines that launch this pipeline, ordered alphabetically for display.</summary>
    public List<PipelineDependencyReferenceDto> Parents { get; init; } = [];
}

public sealed record PipelineDependencyReferenceDto(int? Id, string Name);

public sealed record PipelineRunSummaryDto
{
    public int Id { get; init; }
    public PipelineStatus Status { get; init; }
    public DateTime StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public string? ServerName { get; init; }
    public string? ServerOs { get; init; }
    /// <summary>M: the step a non-terminal run is currently on ("Stage · Step"); null for terminal runs.</summary>
    public string? CurrentStep { get; init; }
}

[AtMostOneOwner]
public sealed record CreatePipelineRequest
{
    [Required]
    [StringLength(100)]
    public string Name { get; init; } = string.Empty;

    [StringLength(500)]
    public string Description { get; init; } = string.Empty;

    [Required]
    [StringLength(50000)]
    public string YamlDefinition { get; init; } = string.Empty;

    public int? ProjectId { get; init; }
    public int? SourceRepositoryId { get; init; }
    [StringLength(255)]
    public string? SourceBranch { get; init; }
    public int? EnvironmentId { get; init; }
    public int? ProjectServerId { get; init; }
}

[AtMostOneOwner]
public sealed record UpdatePipelineRequest
{
    [Required]
    [StringLength(100)]
    public string Name { get; init; } = string.Empty;

    [StringLength(500)]
    public string Description { get; init; } = string.Empty;

    [Required]
    [StringLength(50000)]
    public string YamlDefinition { get; init; } = string.Empty;

    public int? ProjectId { get; init; }
    public int? SourceRepositoryId { get; init; }
    [StringLength(255)]
    public string? SourceBranch { get; init; }
    public int? EnvironmentId { get; init; }
    public int? ProjectServerId { get; init; }
}

/// <summary>Query for a page of pipeline runs: paging (inherited), sorting (inherited
/// <see cref="PaginationRequest.SortBy"/> / <see cref="PaginationRequest.SortDescending"/>) and the
/// column filters the runs grid exposes.
/// <para>Sorting and filtering are applied server-side because the grid is server-paged: applying
/// them to the loaded page only would sort 25 rows and call it an order, which reads as a working
/// affordance while telling the reader something false about the rest of the history.</para></summary>
public sealed record PipelineRunPaginationRequest : PaginationRequest
{
    public PipelineStatus? Status { get; init; }

    [StringLength(255)]
    public string? BranchName { get; init; }

    [StringLength(64)]
    public string? CommitHash { get; init; }
}

public sealed record PipelineRunDto
{
    public int Id { get; init; }
    public int PipelineId { get; init; }
    public int? ProjectId { get; init; }
    public string PipelineName { get; init; } = string.Empty;
    public PipelineStatus Status { get; init; }
    public DateTime StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public Dictionary<string, string> ResolvedVariables { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
    /// <summary>True after the cancellation request has been durably accepted, while mandatory
    /// teardown may still keep the run active.</summary>
    public bool CancellationRequested { get; init; }
    /// <summary>Internal projection bridge used to hydrate <see cref="Warnings"/> after EF materialization.</summary>
    [JsonIgnore]
    public string? ProjectedWarningsJson { get; init; }
    public List<PipelineStepRunDto> Steps { get; init; } = [];
    public List<PipelineApprovalDto> Approvals { get; init; } = [];
    public List<PipelineArtifactDto> Artifacts { get; init; } = [];
    public PipelineTestResultSummaryDto? TestResultSummary { get; init; }
    public PipelineCoverageSummaryDto? CoverageSummary { get; init; }
    public PipelineLintSummaryDto? LintSummary { get; init; }

    /// <summary>Generic per-run metrics (coverage, complexity/CRAP, LOC, …) published by metric steps.
    /// Empty when no metric step ran. Rendered by the UI according to each metric's <see cref="RunMetricDto.Type"/>.</summary>
    public List<RunMetricDto> Metrics { get; init; } = [];

    // Git context (surfaced in the run view). Releases that reference this run are fetched
    // separately via GET /api/releases/by-run/{runId} to keep Pipelines decoupled from Releases.
    public string? BranchName { get; init; }
    public string? CommitHash { get; init; }
    public string? RepositoryUrl { get; init; }
    public string? ProjectName { get; init; }

    /// <summary>Worst analysis grade recorded for this run (A..F), or null when no gate evaluated it.
    /// Projected from the stored <c>AnalysisEvaluation</c> rows, never recomputed, so run listings can
    /// show the gate letter without loading the full gate for every row.</summary>
    public AnalysisGrade? GateGrade { get; init; }

    // Resolved internal git-graph links (when a graph node exists for the run's commit/branch).
    // Empty lists fall back to plain text / the external repo URL in the run view.
    public List<CommitLinkDto> Commits { get; init; } = [];
    public List<BranchLinkDto> Branches { get; init; } = [];

    /// <summary>The exact pipeline YAML executed by this run, captured at trigger time (ADR-015).
    /// Surfaced read-only on the run view for reproducibility/debugging. Null for legacy runs.</summary>
    public string? YamlSnapshot { get; init; }

    /// <summary>Queue-time run parameters (P): the effective name→value pairs the user supplied (or
    /// the parameter defaults) at launch. Empty when the pipeline declares no <c>parameters:</c>.
    /// Surfaced on the run view and used to pre-fill the rerun dialog.</summary>
    public Dictionary<string, string> Parameters { get; init; } = [];
}

/// <summary>A single declared run parameter, returned by <c>GET /api/pipelines/{id}/parameters</c> so
/// the UI can render the queue-time input dialog from the authoritative (git-first) pipeline YAML.</summary>
public sealed record PipelineRunParameterDto
{
    public string Name { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Type { get; init; } = "string";
    public string? Default { get; init; }
    public bool Required { get; init; }
    public string? Description { get; init; }
    public List<string> AllowedValues { get; init; } = [];
}

public sealed record PipelineStepRunDto
{
    public int Id { get; init; }
    public string StepName { get; init; } = string.Empty;
    public string StageName { get; init; } = string.Empty;
    public TaskExecutionStatus Status { get; init; }
    public int? ServerId { get; init; }
    public string? ServerName { get; init; }
    public string? ServerOs { get; init; }
    public DateTime? StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public int? ExitCode { get; init; }
    /// <summary>Structured terminal failure category reported by the agent for the linked task.</summary>
    public string? FailureCode { get; init; }
    /// <summary>Masked terminal diagnostic reported by the agent when execution could not emit logs.</summary>
    public string? FailureReason { get; init; }
    public Dictionary<string, string> OutputVariables { get; init; } = [];
    public int RetryCount { get; init; }
    public bool ContinueOnError { get; init; }
    public string? MatrixLeg { get; init; }
    public bool IsSystem { get; init; }
    public string? GroupName { get; init; }
    /// <summary>The condition that evaluated to false when this step did not run.</summary>
    public string? SkippedCondition { get; init; }
    /// <summary>Non-secret variable values used to evaluate <see cref="SkippedCondition"/>.</summary>
    public Dictionary<string, string> SkippedConditionVariables { get; init; } = [];
    /// <summary>Linked task ID for fetching execution logs via <c>GET /api/logs/task/{taskId}</c>.</summary>
    public int? TaskId { get; init; }

    /// <summary>One-based position of this assigned task in its runner's live queue.</summary>
    public int? QueuePosition { get; init; }

    /// <summary>Total tasks currently waiting in the same runner queue.</summary>
    public int? QueueDepth { get; init; }

    /// <summary>The resolved shell command dispatched to the agent for this step, with secret
    /// values masked. Null for steps that never produced a task (e.g. skipped/pending). Surfaced
    /// above the step log output (S-UX-18).</summary>
    public string? Command { get; init; }

    /// <summary>True when this step ran inside an ephemeral hardened container (the linked task
    /// carried a container image). Drives the "container" badge in the run view (S-DES-26).</summary>
    public bool IsContainerIsolated { get; init; }

    /// <summary><c>type: trigger</c> steps: the id of the child pipeline run this step launched (and
    /// waits on). Null for ordinary steps. The run logs view follows it to nest the child pipeline's
    /// own timeline and logs under this step, so an orchestration run shows every triggered pipeline's
    /// tasks - not just an opaque "waiting" step.</summary>
    public int? TriggeredRunId { get; init; }
}

public sealed record PipelineYamlDefinition
{
    public string Name { get; init; } = string.Empty;
    public string Trigger { get; init; } = "manual";

    /// <summary>Canonical branch used when a manual or automated run does not provide an explicit
    /// queue-time override. This keeps scheduled pipelines reproducible without DB-only configuration.</summary>
    public string? SourceBranch { get; init; }

    /// <summary>Optional project type (<c>dotnet</c>, <c>node</c>, …) declared via the YAML
    /// <c>project_type:</c> key (S-TECH-53). Surfaced as the <c>PROJECT_TYPE</c> variable so step
    /// commands and conditions can auto-adapt to the project structure.</summary>
    public string? ProjectType { get; init; }

    public string? Schedule { get; init; }

    /// <summary>Webhook-only latest-wins policy. A newer matching push requests cancellation of
    /// active runs (while preserving their <c>always()</c> teardown) before launching the new SHA.</summary>
    public bool? SupersedeRunning { get; init; }

    /// <summary>Branch filter for the <c>webhook</c> trigger. When non-empty, an automated webhook run
    /// is created only if the pushed ref matches one of these patterns; an empty list fires on any
    /// branch (backward-compatible). Each entry is an exact branch name (<c>main</c>) or a glob following
    /// GitHub Actions semantics: <c>*</c> matches within one path segment (<c>release/*</c>) and <c>**</c>
    /// matches across segments (<c>release/**</c>). Matching is case-sensitive and ignores the
    /// <c>refs/heads/</c> prefix (on both the ref and the pattern); only applies to the webhook trigger.</summary>
    public List<string> Branches { get; init; } = [];

    public string? Extends { get; init; }
    public List<PipelineTemplateParameterDefinition> Parameters { get; init; } = [];
    public Dictionary<string, string> Variables { get; init; } = [];
    public List<string> VariableLibraries { get; init; } = [];
    public List<string> Vaults { get; init; } = [];
    public List<PipelineStageDefinition> Stages { get; init; } = [];
    /// <summary>Run-level execution isolation, inherited by every stage that doesn't set its own.
    /// This is the source of truth for whether the whole run executes in containers (Phase 2):
    /// when <c>mode: container</c>, the system prepare/cleanup and all steps run in ephemeral
    /// hardened containers sharing the run workspace.</summary>
    public PipelineIsolationDefinition? Isolation { get; init; }

    /// <summary>Pipeline chaining: downstream pipelines to trigger when THIS run succeeds. Each entry
    /// names a target pipeline in the same project and an optional release selector forwarded to the
    /// downstream run as <c>UPSTREAM_RELEASE</c> (so a downstream <c>type: deploy</c> can deploy it).</summary>
    public List<PipelineDownstreamTrigger> OnSuccess { get; init; } = [];
}

/// <summary>One downstream-trigger entry under a pipeline's <c>on_success:</c> list.</summary>
public sealed record PipelineDownstreamTrigger
{
    /// <summary>Target pipeline name (resolved within the upstream run's project).</summary>
    public string Pipeline { get; init; } = string.Empty;

    /// <summary>Optional release selector (id / version / "latest") forwarded as <c>UPSTREAM_RELEASE</c>.</summary>
    public string? Release { get; init; }
}

public sealed record PipelineTemplateParameterDefinition
{
    public string Name { get; init; } = string.Empty;
    /// <summary>Input type: <c>string</c> | <c>number</c> | <c>boolean</c> | <c>choice</c>.</summary>
    public string Type { get; init; } = "string";
    public string? Default { get; init; }
    /// <summary>Allowed values for <c>type: choice</c>.</summary>
    public List<string> AllowedValues { get; init; } = [];
    /// <summary>Human-friendly label shown in the run dialog; falls back to <see cref="Name"/>.</summary>
    public string? DisplayName { get; init; }
    /// <summary>When true, a value must be supplied at queue time (unless a <see cref="Default"/> exists).</summary>
    public bool Required { get; init; }
    /// <summary>Optional help text shown under the field.</summary>
    public string? Description { get; init; }
}

public sealed record PipelineStageDefinition
{
    public string Name { get; init; } = string.Empty;
    public bool Remove { get; init; }
    /// <summary>Agent for direct-step stages (legacy format). Inherited by jobs as default.</summary>
    public string Agent { get; init; } = string.Empty;
    /// <summary>Required OS family for this stage (<c>linux</c> / <c>windows</c>). Empty = no constraint.
    /// A run fails if no online runner of this OS is available. Inherited by jobs as default.</summary>
    public string? Os { get; init; }
    public string? Group { get; init; }
    /// <summary>Trusted execution classification propagated to the agent. Supported values are
    /// <c>build</c> and <c>deploy</c>. The role classifies the workload for agent policy; only a native
    /// <c>type: deploy</c> step requires and is routed to a cross-agent deployment target.</summary>
    public string? ExecutionRole { get; init; }
    public string? Environment { get; init; }
    public string? Pool { get; init; }
    public string? Condition { get; init; }
    public List<string> DependsOn { get; init; } = [];
    public Dictionary<string, string> Variables { get; init; } = [];
    /// <summary>Direct steps (legacy format). Ignored when <see cref="Jobs"/> is populated.</summary>
    public List<PipelineStepDefinition> Steps { get; init; } = [];
    /// <summary>Azure DevOps-style jobs. When populated, each job runs as an independent unit.</summary>
    public List<PipelineJobDefinition> Jobs { get; init; } = [];
    public Dictionary<string, List<string>>? Matrix { get; init; }
    public PipelineDeploymentStrategy? Strategy { get; init; }
    public List<string> Artifacts { get; init; } = [];
    /// <summary>Execution isolation for this stage. Null / <c>mode: process</c> runs steps directly
    /// on the agent (inside its systemd sandbox). <c>mode: container</c> runs each step in an
    /// ephemeral hardened container - requires a Docker-capable runner or the run is blocked.
    /// Inherited by jobs as the default when they don't set their own.</summary>
    public PipelineIsolationDefinition? Isolation { get; init; }
}

/// <summary>A job inside a stage - carries its own agent/pool, steps, and optional matrix.</summary>
public sealed record PipelineJobDefinition
{
    public string Name { get; init; } = string.Empty;
    public bool Remove { get; init; }
    public string Agent { get; init; } = string.Empty;
    /// <summary>Required OS family for this job (<c>linux</c> / <c>windows</c>). Overrides the stage's
    /// <c>os</c> when set; empty inherits the stage value.</summary>
    public string? Os { get; init; }
    public string? Pool { get; init; }
    public string? Environment { get; init; }
    public string? ExecutionRole { get; init; }
    public string? Condition { get; init; }
    /// <summary>Sibling job names within the same stage this job depends on (intra-stage DAG, S-TECH-55).
    /// Empty = the job runs as soon as the stage's own prerequisites are met (parallel with siblings).</summary>
    public List<string> DependsOn { get; init; } = [];
    public Dictionary<string, string> Variables { get; init; } = [];
    public List<PipelineStepDefinition> Steps { get; init; } = [];
    public Dictionary<string, List<string>>? Matrix { get; init; }
    public PipelineDeploymentStrategy? Strategy { get; init; }
    public List<string> Artifacts { get; init; } = [];
    /// <summary>Execution isolation for this job. Overrides the stage's <c>isolation</c> when set.</summary>
    public PipelineIsolationDefinition? Isolation { get; init; }
}

public sealed record PipelineDeploymentStrategy
{
    public string Type { get; init; } = "runOnce";
    public int MaxParallel { get; init; } = 1;
}

