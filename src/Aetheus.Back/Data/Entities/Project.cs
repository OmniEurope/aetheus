// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Data.Entities;

public class Project
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? RepositoryUrl { get; set; }
    public string? DefaultBranch { get; set; }
    public ProjectStatus Status { get; set; } = ProjectStatus.Active;
    public string Tags { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // S-FEAT-15: per-project artifact retention overrides (days). Null falls back to the global
    // ArtifactStorage:DefaultRetentionDays / LatestRetentionDays config values.
    public int? ArtifactRetentionDays { get; set; }
    public int? ArtifactLatestRetentionDays { get; set; }

    // S-FEAT-16: default release version pattern used when a `type: release` step omits `version:`.
    // Null falls back to the built-in "1.0.$(BUILD_BUILDID)". Supports $(VAR) substitution.
    public string? ReleaseNumberingPattern { get; set; }

    public int OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;

    // External-Git parity (Features:ExternalRepos). Source discriminant: when set, this project's
    // repository is an external mirror-backed connection; otherwise the source is the internal
    // GitInternalRepo / RepositoryUrl. An external repository attached beside the internal one (recette
    // R-534) leaves this null: it is an additional source a pipeline names, not the project's source.
    public int? GitConnectionId { get; set; }
    public GitConnection? GitConnection { get; set; }

    // Navigation
    public List<Pipeline> Pipelines { get; set; } = [];
    public List<ProjectServer> ProjectServers { get; set; } = [];
}
