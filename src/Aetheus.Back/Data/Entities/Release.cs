// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Data.Entities;

public class Release
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public string Version { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;
    public ReleaseStatus Status { get; set; } = ReleaseStatus.Detected;
    public DateTime DetectedAt { get; set; }
    public DateTime? PublishedAt { get; set; }
    public DateTime? PromotedAt { get; set; }
    public DateTime? RolledBackAt { get; set; }
    public int? PipelineRunId { get; set; }
    public string? Changelog { get; set; }
    public int BuildNumber { get; set; }
    public string? CommitHash { get; set; }
    public string? TagName { get; set; }

    // Navigation
    public Project Project { get; set; } = null!;
    public PipelineRun? PipelineRun { get; set; }

    // Cross-linking (many-to-many): a release can bundle several artifacts, span several commits,
    // and relate to several branches. See GitCommit / GitBranch.
    public List<PipelineArtifact> Artifacts { get; set; } = [];
    public List<GitCommit> Commits { get; set; } = [];
    public List<GitBranch> Branches { get; set; } = [];
    public List<ReleaseRollback> RollbacksFrom { get; set; } = [];
    public List<ReleaseRollback> RollbacksTo { get; set; } = [];
}
