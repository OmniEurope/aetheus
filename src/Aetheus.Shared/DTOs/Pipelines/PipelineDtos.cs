// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;
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

[ExactlyOneOwner]
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
    [StringLength(255)]
    public string? SourceBranch { get; init; }
    public int? EnvironmentId { get; init; }
    public int? ProjectServerId { get; init; }
}

[ExactlyOneOwner]
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
    [StringLength(255)]
    public string? SourceBranch { get; init; }
    public int? EnvironmentId { get; init; }
    public int? ProjectServerId { get; init; }
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
    public Dictionary<string, string> OutputVariables { get; init; } = [];
    public int RetryCount { get; init; }
    public bool ContinueOnError { get; init; }
    public string? MatrixLeg { get; init; }
    public bool IsSystem { get; init; }
    public string? GroupName { get; init; }
    /// <summary>Linked task ID for fetching execution logs via <c>GET /api/logs/task/{taskId}</c>.</summary>
    public int? TaskId { get; init; }

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

/// <summary>
/// Per-stage/job execution isolation. <c>Mode</c> = <c>process</c> (default) or <c>container</c>.
/// In container mode each step runs in an ephemeral, hardened container (cap-drop ALL, read-only
/// root, pids/memory limits). <c>Runtime</c> selects the container runtime (<c>runc</c> default,
/// <c>runsc</c> for gVisor, <c>kata</c> for micro-VMs) so kernel-level isolation can be enabled
/// without a code change.
/// </summary>
public sealed record PipelineIsolationDefinition
{
    public const string ModeProcess = "process";
    public const string ModeContainer = "container";

    /// <summary><c>process</c> (default) or <c>container</c>.</summary>
    public string Mode { get; init; } = ModeProcess;

    /// <summary>Container image when <see cref="Mode"/> is <c>container</c> (e.g.
    /// <c>mcr.microsoft.com/dotnet/sdk:9.0</c>). Required for container mode.</summary>
    public string? Image { get; init; }

    /// <summary>Container runtime: <c>runc</c> (default), <c>runsc</c> (gVisor), <c>kata</c>.</summary>
    public string? Runtime { get; init; }

    /// <summary>Container network: <c>bridge</c> (default - outbound access, needed for clone /
    /// package restore) or <c>none</c> to fully cut the container off from the network.</summary>
    public string? Network { get; init; }

    /// <summary>Optional memory ceiling for the container (S-UX-35), maps to <c>docker run --memory</c>
    /// (e.g. <c>512m</c>, <c>2g</c>). Null = unbounded.</summary>
    public string? Memory { get; init; }

    /// <summary>Optional CPU quota for the container (S-UX-35), maps to <c>docker run --cpus</c>
    /// (e.g. <c>1.5</c>). Null = unbounded.</summary>
    public string? Cpus { get; init; }

    public bool IsContainer => string.Equals(Mode, ModeContainer, StringComparison.OrdinalIgnoreCase);
}

public sealed record PipelineDeploymentStrategy
{
    public string Type { get; init; } = "runOnce";
    public int MaxParallel { get; init; } = 1;
}

public sealed record PipelineStepDefinition
{
    public string Name { get; init; } = string.Empty;
    public bool Remove { get; init; }
    public string Shell { get; init; } = string.Empty;
    public string? Type { get; init; }
    public string? Condition { get; init; }
    public bool Checkout { get; init; }
    public string? WorkingDirectory { get; init; }
    public int TimeoutSeconds { get; init; } = 300;
    public int RetryCount { get; init; }
    public bool ContinueOnError { get; init; }
    public string? Version { get; init; }
    public bool Changelog { get; init; }
    /// <summary>Marks a release created after a verified production go-live as the project's active
    /// deployment. The backend demotes the previously active release atomically.</summary>
    public bool Deployed { get; init; }
    public List<string> TargetFiles { get; init; } = [];

    /// <summary>S-FEAT-K3P8: minimum line-coverage percent (0–100) for a <c>type: coverage</c> step.
    /// When set, the step fails if the published line rate is below it (respecting continue_on_error).</summary>
    public double? MinCoverage { get; init; }

    /// <summary>S-FEAT-D7M5: maximum allowed cyclomatic complexity for a <c>type: complexity</c> step.
    /// When set, the step fails if the highest method CC exceeds it (respecting continue_on_error).</summary>
    public int? MaxComplexity { get; init; }

    // ── Cross-agent deploy (type: deploy) ──────────────────────────────────────────────────────────
    // The four fields below configure a deployment step. The backend resolves which artifact to ship
    // from Artifact (same-run) OR Release (an existing release), infers DeployKind from Compose, and
    // dispatches an OperationKind.PipelineDeploy to a deployment-capable agent. App is the application
    // instance name (validated against OperationTargetValidator.DeployAppRegex at dispatch).

    /// <summary>Same-run artifact name to deploy (produced by an earlier <c>type: artifacts</c> step in
    /// this run). Mutually informative with <see cref="Release"/>: when both are null the backend fails
    /// the step with a clear "nothing to deploy" error.</summary>
    public string? Artifact { get; init; }

    /// <summary>Relative destination used by <c>type: restore-artifacts</c>. The control plane and
    /// agent both reject rooted paths and traversal segments. Null restores into the workspace root.</summary>
    public string? TargetDirectory { get; init; }

    /// <summary>Bootstrap-only escape hatch for <c>type: restore-artifacts</c> with the literal
    /// <c>release: latest-published</c>, <c>release: previous-published</c>, or
    /// <c>release: previous-deployed</c>. When true and no retained
    /// rollback-capable artifact exists for that selector, the
    /// step succeeds explicitly without dispatching an agent task so later steps can prove a first
    /// contract-release bootstrap. Imported/tag-only release metadata does not disable this bootstrap.
    /// It never suppresses an invalid selector or a failed artifact download.</summary>
    public bool AllowMissing { get; init; }

    /// <summary>Existing release to deploy (scenario 3): a release id, a version string, or the literal
    /// <c>"latest"</c> to resolve the newest release of the run's project, or
    /// <c>"latest-published"</c> to resolve the newest retained publishable/deployed release, or
    /// <c>"previous-published"</c> to resolve the newest published one whose artifact commit differs from
    /// the current run, or <c>"previous-deployed"</c> to resolve the active deployed predecessor at a
    /// different commit. Takes the
    /// artifact from that release instead of the current run. Also supported by
    /// <c>type: restore-artifacts</c> for an exact retained N-1 payload.</summary>
    public string? Release { get; init; }

    /// <summary>Application instance name on the target agent. Doubles as the systemd template instance
    /// (<c>aetheus-app@&lt;app&gt;</c>) and the on-disk deploy directory, so it is validated against the
    /// strict <c>^[a-zA-Z0-9_-]{1,64}$</c> shape (<see cref="OperationTargetValidator.DeployAppRegex"/>)
    /// before dispatch and re-validated by the root-owned restart helper agent-side.</summary>
    public string? App { get; init; }

    /// <summary>Path (within the artifact) to a docker compose file. When set, the deploy runs in
    /// container mode (<c>docker load</c> + <c>docker compose up -d --wait</c>); when null, binary mode
    /// (atomic symlink flip + systemd restart helper). This is how <c>DeployKind</c> is inferred.</summary>
    public string? Compose { get; init; }

    /// <summary>Dedicated post-deploy health-gate stabilization window (seconds) for a binary
    /// <c>type: deploy</c> step. When &gt; 0 the agent uses it verbatim (clamped to a 20s floor and the
    /// overall step timeout) instead of deriving the window from <see cref="TimeoutSeconds"/>/3 - so a
    /// legitimately slow .NET cold start (JIT + EF migrations) is not mistaken for a crash loop and
    /// rolled back. 0 (default) keeps the timeout-derived window. Maps to YAML <c>health_timeout_seconds</c>.</summary>
    public int HealthTimeoutSeconds { get; init; }

    /// <summary>Optional functional readiness URL for a binary <c>type: deploy</c> step. It must be
    /// an absolute loopback HTTP(S) URL. After systemd is stable the target agent requires a 2xx
    /// response before declaring success; failure follows the same automatic rollback path. Maps to
    /// YAML <c>health_url</c>.</summary>
    public string? HealthUrl { get; init; }

    /// <summary>Backup-run selector for <c>type: restore-backup</c>. Manual rollbacks pass the
    /// verified run through <c>$(AETHEUS_ROLLBACK_BACKUP_RUN_ID)</c>; the backend resolves it rather
    /// than trusting a file path supplied by YAML.</summary>
    public string? BackupRun { get; init; }

    // ── Host Apache reverse proxy (type: apache-proxy) ───────────────────────────────────────────────
    // The backend renders a reverse-proxy vhost (ServerName → Upstream), then dispatches an
    // OperationKind.ApacheConfigureProxy to an Apache-manage-capable agent that writes the vhost,
    // enables the site and gracefully reloads Apache on the host.

    /// <summary>type: apache-proxy - the <c>ServerName</c> for the rendered reverse-proxy vhost
    /// (the public host/domain). Maps to YAML <c>server_name</c>.</summary>
    public string? ServerName { get; init; }

    /// <summary>type: apache-proxy - the upstream the vhost proxies to, e.g.
    /// <c>http://127.0.0.1:8090</c> (the published port of the deployed container). Maps to <c>upstream</c>.</summary>
    public string? Upstream { get; init; }

    // ── Host HTTPS via Certbot (type: certbot) ───────────────────────────────────────────────────────

    /// <summary>type: certbot - comma-separated domain(s) to obtain a certificate for. Maps to
    /// <c>domains</c>. On a host that cannot complete ACME validation (no public DNS / unreachable
    /// port 80) the agent falls back to a self-signed certificate in the Let's Encrypt layout so the
    /// site still serves HTTPS ("do the closest thing").</summary>
    public string? Domains { get; init; }

    /// <summary>type: certbot - contact email for the ACME account / expiry notices. Maps to <c>email</c>.</summary>
    public string? Email { get; init; }

    // ── Pipeline orchestration (type: trigger) ──────────────────────────────────────────────────────

    /// <summary>type: trigger - the name of another pipeline (same project) to trigger and wait for.
    /// The step stays Running until the triggered child run reaches a terminal status, then mirrors it
    /// (child Success ⇒ step Success; child Failed/Cancelled ⇒ step Failed, respecting
    /// <see cref="ContinueOnError"/>). Maps to YAML <c>pipeline</c>.</summary>
    public string? Pipeline { get; init; }

    /// <summary>type: trigger - extra non-secret variables passed to the child run. System lineage
    /// variables are applied afterwards and cannot be overridden. Maps to YAML <c>variables</c>.</summary>
    public Dictionary<string, string> Variables { get; init; } = [];

    /// <summary>type: restore-artifacts - name of the upstream pipeline, triggered by the direct
    /// parent orchestration run, that produced <see cref="Artifact"/>. Maps to
    /// YAML <c>artifact_source_pipeline</c>.</summary>
    public string? ArtifactSourcePipeline { get; init; }
}

public sealed record DryRunResultDto
{
    public List<DryRunStageDto> Stages { get; init; } = [];
    public Dictionary<string, string> ResolvedVariables { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
}

public sealed record DryRunStageDto
{
    public string StageName { get; init; } = string.Empty;
    public string Agent { get; init; } = string.Empty;
    public string? Os { get; init; }
    public List<DryRunStepDto> Steps { get; init; } = [];
}

public sealed record DryRunStepDto
{
    public string StepName { get; init; } = string.Empty;
    public string OriginalCommand { get; init; } = string.Empty;
    public string ResolvedCommand { get; init; } = string.Empty;
}

/// <summary>Result of resolving each stage's target server <em>before</em> launching a run,
/// so the UI can warn when no online agent matches a stage.</summary>
public sealed record PipelinePreflightDto
{
    public List<PreflightStageDto> Stages { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
}

public sealed record PreflightStageDto
{
    public string StageName { get; init; } = string.Empty;
    public PreflightTargetKind TargetKind { get; init; }

    /// <summary>The agent name / pool name / environment name the stage targets.</summary>
    public string Target { get; init; } = string.Empty;
    public bool Resolved { get; init; }
    public string? ServerName { get; init; }

    /// <summary>Human-readable reason when <see cref="Resolved"/> is false.</summary>
    public string? Reason { get; init; }
}

// Per-run results & metrics DTOs (test / coverage / lint / complexity, trends, RunMetric) live in
// PipelineResultDtos.cs to keep this file within the 600-line budget.

public sealed record YamlValidationResultDto
{
    public bool IsValid { get; init; }
    public PipelineYamlDefinition? Definition { get; init; }
    public List<string> Errors { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
}

public sealed record ValidateYamlRequest
{
    [Required]
    [StringLength(50_000)]
    public string Yaml { get; init; } = string.Empty;

    public int? OrganizationId { get; init; }
}
