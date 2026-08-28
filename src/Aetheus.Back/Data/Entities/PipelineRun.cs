// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Data.Entities;

public class PipelineRun
{
    public int Id { get; set; }
    public int PipelineId { get; set; }
    public PipelineStatus Status { get; set; } = PipelineStatus.Pending;
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string AdditionalVariablesJson { get; set; } = "{}";
    public string? ResolvedVariablesJson { get; set; }
    public string? WarningsJson { get; set; }

    /// <summary>Queue-time run parameters (P): JSON of the effective name→value pairs the user supplied
    /// (or the parameter defaults) at launch. "{}" when the pipeline declares no <c>parameters:</c>.
    /// Snapshotted so the run view and the rerun dialog can show/replay exactly what was launched.</summary>
    public string ParametersJson { get; set; } = "{}";

    /// <summary>
    /// The exact pipeline YAML executed by this run, captured at trigger time. For project-owned
    /// pipelines this is the git-strict definition read from the project repo's <c>.pipeline/</c>;
    /// makes the run reproducible/auditable even after the definition later changes. Null for runs
    /// created before snapshotting existed.
    /// </summary>
    public string? YamlSnapshot { get; set; }

    /// <summary>Branch this run built against (resolved at trigger time from the project's
    /// default branch or the triggering push). Null for runs created before git context existed.</summary>
    public string? BranchName { get; set; }

    /// <summary>Commit SHA this run built against. Workspace runs require a full immutable SHA
    /// before creation; null is reserved for pipelines that do not need a repository workspace.</summary>
    public string? CommitHash { get; set; }

    /// <summary>The canonical clone URL selected together with the authoritative pipeline YAML.
    /// Snapshotted so every stage checkout uses the same repository even if project settings change.</summary>
    public string? RepositoryUrl { get; set; }

    /// <summary>Caller-supplied key used to return the original run when a launch request is replayed.</summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>
    /// Per-pipeline sequential build number reserved from <see cref="Pipeline.BuildCounter"/> at
    /// launch, exposed to steps as <c>BUILD_PIPELINE_RUNNUMBER</c>. Unlike the globally monotonic
    /// run id (<c>BUILD_BUILDID</c>) this increments by exactly one per run of the same pipeline,
    /// which is what an application version pattern needs. 0 for runs created before it existed.
    /// </summary>
    public int BuildNumber { get; set; }

    // Navigation
    public Pipeline Pipeline { get; set; } = null!;
    public List<PipelineStepRun> StepRuns { get; set; } = [];
    public List<ServerTask> Tasks { get; set; } = [];
    public List<TestResult> TestResults { get; set; } = [];
    public List<CoverageResult> CoverageResults { get; set; } = [];
    public List<LintResult> LintResults { get; set; } = [];
    public List<RunMetric> RunMetrics { get; set; } = [];
    public List<PipelineArtifact> Artifacts { get; set; } = [];

    /// <summary>Analysis evaluations recorded against this run. The inverse of an existing foreign key,
    /// so it adds no column and no migration; it exists so run listings can project the gate grade in
    /// the same query instead of computing a gate per row.</summary>
    public List<AnalysisEvaluation> AnalysisEvaluations { get; set; } = [];
}
