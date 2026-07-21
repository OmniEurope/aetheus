// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.Enums;

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

    /// <summary>Commit SHA this run built against, resolved best-effort at trigger time from the
    /// project repo's branch head. Null when the repo/commit could not be resolved.</summary>
    public string? CommitHash { get; set; }

    // Navigation
    public Pipeline Pipeline { get; set; } = null!;
    public List<PipelineStepRun> StepRuns { get; set; } = [];
    public List<ServerTask> Tasks { get; set; } = [];
    public List<TestResult> TestResults { get; set; } = [];
    public List<CoverageResult> CoverageResults { get; set; } = [];
    public List<LintResult> LintResults { get; set; } = [];
    public List<RunMetric> RunMetrics { get; set; } = [];
    public List<PipelineArtifact> Artifacts { get; set; } = [];
}
