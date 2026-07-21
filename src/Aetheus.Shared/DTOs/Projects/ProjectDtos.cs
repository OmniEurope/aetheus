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

    /// <summary>Most recent git activity for the project: the latest of the internal repository's
    /// last push and any external connection's last sync. Null when the project has no git.
    /// Drives the "last git update" chip on the projects list.</summary>
    public DateTime? LastGitUpdateAt { get; init; }
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

public sealed record CreateProjectRequest
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

    /// <summary>Organization that owns the new project. Optional -
    /// when omitted, the API uses the caller's default organization.</summary>
    [Range(1, int.MaxValue)]
    public int? OrganizationId { get; init; }
}

public sealed record UpdateProjectRequest
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

    public ProjectStatus Status { get; init; }
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

public sealed record ProjectActivityDto
{
    public string Type { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Status { get; init; }
    public DateTime Timestamp { get; init; }
    public string? Icon { get; init; }
}
