// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.Enums;

namespace Aetheus.Back.Data.Entities;

public class PipelineArtifact
{
    public int Id { get; set; }
    public int PipelineRunId { get; set; }
    public int PipelineId { get; set; }
    public int? ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    // SHA-256 of the stored content, computed while streaming to disk. Null for metadata-only
    // publishes (no bytes ever reached the backend) - an honest "unknown", never a fabricated value.
    public string? Sha256 { get; set; }
    public string? StageName { get; set; }
    public string? StepName { get; set; }
    public DateTime CreatedAt { get; set; }

    // Retention
    public ArtifactRetentionPolicy RetentionPolicy { get; set; } = ArtifactRetentionPolicy.Build;
    public DateTime RetentionExpiresAt { get; set; }

    // Deployment context
    public string? EnvironmentName { get; set; }

    // Navigation
    public PipelineRun PipelineRun { get; set; } = null!;
    public Pipeline Pipeline { get; set; } = null!;
    public Project? Project { get; set; }

    // Cross-linking (many-to-many): an artifact can belong to several releases and relate to
    // several commits / branches. See GitCommit / GitBranch.
    public List<Release> Releases { get; set; } = [];
    public List<GitCommit> Commits { get; set; } = [];
    public List<GitBranch> Branches { get; set; } = [];
}
