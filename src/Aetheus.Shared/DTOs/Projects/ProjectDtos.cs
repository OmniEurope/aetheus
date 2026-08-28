// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;
using Aetheus.Shared.Enums;
using Aetheus.Shared.Validation;

namespace Aetheus.Shared.DTOs;

public sealed record ProjectDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string? RepositoryUrl { get; init; }
    public int? InternalRepositoryId { get; init; }
    public string? DefaultBranch { get; init; }
    public ProjectStatus Status { get; init; }
    public List<string> Tags { get; init; } = [];
    public int PipelineCount { get; init; }
    public int OrganizationId { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }

    /// <summary>Status of the most recent pipeline run across all of the project's pipelines, or
    /// null when the project has never been run. Drives the last-run badge on the projects list
    /// (S-UX-30).</summary>
    public PipelineStatus? LastRunStatus { get; init; }
    public DateTime? LastRunAt { get; init; }
    public int? LastRunId { get; init; }
    public string? LastRunName { get; init; }
    public int? ParentRunId { get; init; }
    public string? ParentRunName { get; init; }

    /// <summary>Current project-wide A-F grade, combining the latest required analysis domains.
    /// Null when no complete grade has been produced yet.</summary>
    public AnalysisGrade? LatestGateGrade { get; init; }

    /// <summary>Most recent git activity for the project: the latest of the internal repository's
    /// last push and any external connection's last sync. Null when the project has no git.
    /// Drives the "last git update" chip on the projects list.</summary>
    public DateTime? LastGitUpdateAt { get; init; }

    /// <summary>Most recent first-class commit observed for the project. The id deep-links to the
    /// commit detail while the SHA/message/date keep the project tile useful without another call.</summary>
    public int? LastCommitId { get; init; }
    public string? LastCommitSha { get; init; }
    public string? LastCommitMessage { get; init; }
    public DateTime? LastCommitAt { get; init; }

    /// <summary>Availability aggregated from enabled production monitored apps. Unavailable means
    /// no production monitor exists or its current state is unknown.</summary>
    public ProjectProductionStatus ProductionStatus { get; init; } = ProjectProductionStatus.Unavailable;

    /// <summary>Active analytics sessions seen in the last five minutes across production apps.
    /// Null means web analytics is not available; zero is a measured absence of active sessions.</summary>
    public int? OnlineUserCount { get; init; }
}

public sealed record ProjectDetailDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string? RepositoryUrl { get; init; }
    public string? DefaultBranch { get; init; }
    public ProjectStatus Status { get; init; }
    public List<string> Tags { get; init; } = [];
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }

    /// <summary>Current project-wide A-F grade, combining the latest required analysis domains.
    /// Null when no complete grade has been produced yet.</summary>
    public AnalysisGrade? LatestGateGrade { get; init; }

    /// <summary>Most recent git activity (internal last push / external last sync); null when the
    /// project has no git. Shown in the overview "Repository" card.</summary>
    public DateTime? LastGitUpdateAt { get; init; }

    public List<PipelineDto> Pipelines { get; init; } = [];

    // S-FEAT-15 / S-FEAT-16: per-project overrides (null = use the global / built-in default).
    public int? ArtifactRetentionDays { get; init; }
    public int? ArtifactLatestRetentionDays { get; init; }
    public string? ReleaseNumberingPattern { get; init; }

    // S-TECH-N8R3: per-section counts so the project detail can render enriched section tiles
    // (Servers / Releases / Vaults / Libraries / Environments) without N extra round-trips.
    public int ServerCount { get; init; }
    public int ReleaseCount { get; init; }
    public int VaultCount { get; init; }
    public int LibraryCount { get; init; }
    public int EnvironmentCount { get; init; }
}

public abstract record ProjectRequest
{
    [Required]
    [StringLength(100)]
    public string Name { get; init; } = string.Empty;

    [StringLength(500)]
    public string Description { get; init; } = string.Empty;

    [StringLength(500)]
    [HttpsUrl]
    public string? RepositoryUrl { get; init; }

    [StringLength(100)]
    public string? DefaultBranch { get; init; }

    [MaxLength(20)]
    [MaxItemStringLength(50)]
    public List<string> Tags { get; init; } = [];

    [Range(1, 3650)]
    public int? ArtifactRetentionDays { get; init; }

    [Range(1, 3650)]
    public int? ArtifactLatestRetentionDays { get; init; }

    [StringLength(100)]
    public string? ReleaseNumberingPattern { get; init; }

}

public sealed record CreateProjectRequest : ProjectRequest
{
    /// <summary>Organization that owns the new project. Optional -
    /// when omitted, the API uses the caller's default organization.</summary>
    [Range(1, int.MaxValue)]
    public int? OrganizationId { get; init; }
}

public sealed record UpdateProjectRequest : ProjectRequest
{
    public ProjectStatus Status { get; init; }
}

public sealed record ProjectActivityDto
{
    public string Type { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Status { get; init; }
    public DateTime Timestamp { get; init; }
    public string? Icon { get; init; }
}
