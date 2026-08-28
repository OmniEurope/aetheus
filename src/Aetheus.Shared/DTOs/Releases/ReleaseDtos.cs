// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;
using Aetheus.Shared.Enums;

namespace Aetheus.Shared.DTOs;

public sealed record ReleaseDto
{
    public int Id { get; init; }
    public int ProjectId { get; init; }
    public string ProjectName { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string BranchName { get; init; } = string.Empty;
    public ReleaseStatus Status { get; init; }
    public DateTime DetectedAt { get; init; }
    public DateTime? PublishedAt { get; init; }
    public DateTime? PromotedAt { get; init; }
    public DateTime? RolledBackAt { get; init; }
    public int? PipelineRunId { get; init; }
    public int? SourcePipelineId { get; init; }
    public string? SourcePipelineName { get; init; }
    public string? Changelog { get; init; }
    public int BuildNumber { get; init; }
    public string? CommitHash { get; init; }
    public string? TagName { get; init; }
    public string? EnvironmentName { get; init; }

    public string? RepositoryUrl { get; init; }

    // Cross-linking (many-to-many).
    public List<ArtifactLinkDto> Artifacts { get; init; } = [];
    public List<CommitLinkDto> Commits { get; init; } = [];
    public List<BranchLinkDto> Branches { get; init; } = [];
}

public sealed record TriggerReleaseBuildRequest
{
    [Required]
    public int PipelineId { get; init; }
}

public sealed record RollbackReleaseRequest
{
    [Required]
    public int PipelineId { get; init; }

    /// <summary>Dangerous and opt-in: the selected pipeline must contain a restore-backup step and the
    /// supplied backup must already have passed a restore-check.</summary>
    public bool RestoreDatabase { get; init; }

    public int? BackupRunId { get; init; }
}

public sealed record ReleaseRollbackDto
{
    public int Id { get; init; }
    public int SourceReleaseId { get; init; }
    public int TargetReleaseId { get; init; }
    public int? PipelineRunId { get; init; }
    public int? BackupRunId { get; init; }
    public bool RestoreDatabase { get; init; }
    public RollbackStatus Status { get; init; }
    public DateTime RequestedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public string? FailureReason { get; init; }
}

public sealed record ReleaseRollbackPreviewDto
{
    public bool CanRollback { get; init; }
    public string? TargetVersion { get; init; }
    public DateTime? TargetPublishedAt { get; init; }
    public string? Reason { get; init; }
}

public sealed record CreateReleaseRequest
{
    [Required]
    [StringLength(100)]
    public string Version { get; init; } = string.Empty;

    [StringLength(10000)]
    public string? Changelog { get; init; }

    public int? ArtifactId { get; init; }
    public int? PipelineRunId { get; init; }
    public int? ArtifactPipelineRunId { get; init; }

    [StringLength(40)]
    public string? CommitHash { get; init; }

    [StringLength(100)]
    public string? TagName { get; init; }

    [StringLength(200)]
    public string? BranchName { get; init; }

    /// <summary>True only when the release step runs after a verified production go-live.</summary>
    public bool Deployed { get; init; }
}

public sealed record ReleaseCreatedResponse
{
    public int Id { get; init; }
    public string Version { get; init; } = string.Empty;
    public int BuildNumber { get; init; }
}

public sealed record WebhookPayload
{
    [StringLength(200)]
    public string? Ref { get; init; }

    [StringLength(200)]
    public string? RepositoryUrl { get; init; }
}
