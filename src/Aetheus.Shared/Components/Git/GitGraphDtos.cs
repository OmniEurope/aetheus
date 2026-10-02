// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Shared.Components.Git;

/// <summary>Lightweight reference to a release, used to render cross-link tiles.</summary>
public sealed record ReleaseLinkDto
{
    public int Id { get; init; }
    public string Version { get; init; } = string.Empty;
    public ReleaseStatus Status { get; init; }
}

/// <summary>Lightweight reference to an artifact, used to render cross-link tiles.</summary>
public sealed record ArtifactLinkDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public ArtifactRetentionPolicy RetentionPolicy { get; init; }
}

/// <summary>Lightweight reference to a git commit, used to render cross-link tiles.</summary>
public sealed record CommitLinkDto
{
    public int Id { get; init; }
    public string Sha { get; init; } = string.Empty;
    public string? Message { get; init; }
}

/// <summary>Lightweight reference to a git branch, used to render cross-link tiles.</summary>
public sealed record BranchLinkDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
}

/// <summary>Detail of a git commit and everything cross-linked to it (releases, artifacts, branches).</summary>
public sealed record GitCommitDto
{
    public int Id { get; init; }
    public int ProjectId { get; init; }
    public string? ProjectName { get; init; }
    public string Sha { get; init; } = string.Empty;
    public string? Message { get; init; }
    public string? Author { get; init; }
    public DateTime? CommittedAt { get; init; }
    public string? RepositoryUrl { get; init; }
    public List<ReleaseLinkDto> Releases { get; init; } = [];
    public List<ArtifactLinkDto> Artifacts { get; init; } = [];
    public List<BranchLinkDto> Branches { get; init; } = [];
}

/// <summary>Project-scoped commit↔release↔artifact graph (S-FEAT-29). Commits are ordered most-recent
/// first; each carries its cross-linked releases/artifacts/branches for a timeline/DAG view.</summary>
public sealed record ProjectGitGraphDto
{
    public int ProjectId { get; init; }
    public string? ProjectName { get; init; }
    public string? RepositoryUrl { get; init; }
    public List<GitCommitDto> Commits { get; init; } = [];
    public List<BranchLinkDto> Branches { get; init; } = [];
}

/// <summary>Detail of a git branch and everything cross-linked to it (releases, artifacts, commits).</summary>
public sealed record GitBranchDto
{
    public int Id { get; init; }
    public int ProjectId { get; init; }
    public string? ProjectName { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? RepositoryUrl { get; init; }
    public List<ReleaseLinkDto> Releases { get; init; } = [];
    public List<ArtifactLinkDto> Artifacts { get; init; } = [];
    public List<CommitLinkDto> Commits { get; init; } = [];
}
