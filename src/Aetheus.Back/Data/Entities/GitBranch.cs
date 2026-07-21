// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Data.Entities;

/// <summary>
/// A git branch observed for a project. First-class so releases, artifacts and commits can each
/// link to many branches (and a branch to many of each) - the cross-linking is many-to-many.
/// Branches are recorded best-effort from pipeline runs / release detection; absence is not an error.
/// </summary>
public class GitBranch
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    // Navigation
    public Project Project { get; set; } = null!;
    public List<GitCommit> Commits { get; set; } = [];
    public List<Release> Releases { get; set; } = [];
    public List<PipelineArtifact> Artifacts { get; set; } = [];
}
