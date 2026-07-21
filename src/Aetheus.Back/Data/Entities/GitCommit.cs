// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Data.Entities;

/// <summary>
/// A git commit observed for a project, keyed by full SHA. First-class so releases and artifacts
/// can each link to many commits (and a commit to many releases/artifacts) - the cross-linking is
/// many-to-many. A commit can also belong to several branches. Recorded best-effort; absence is
/// not an error.
/// </summary>
public class GitCommit
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public string Sha { get; set; } = string.Empty;
    public string? Message { get; set; }
    public string? Author { get; set; }
    public DateTime? CommittedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation
    public Project Project { get; set; } = null!;
    public List<GitBranch> Branches { get; set; } = [];
    public List<Release> Releases { get; set; } = [];
    public List<PipelineArtifact> Artifacts { get; set; } = [];
}
