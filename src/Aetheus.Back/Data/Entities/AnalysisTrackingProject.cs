// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Data.Entities;

public class AnalysisTrackingProject
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }
    public int ProjectId { get; set; }
    public int? LastSbomReportId { get; set; }
    public string Provider { get; set; } = "dependency-track";
    public string ExternalProjectId { get; set; } = string.Empty;
    public string ExternalProjectName { get; set; } = string.Empty;
    public bool Active { get; set; } = true;
    public string SyncStatus { get; set; } = "Pending";
    public string? LastError { get; set; }
    public string? LastSnapshotHash { get; set; }
    public int LastKnownVulnerabilityCount { get; set; }
    public DateTime? LastSyncAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Organization Organization { get; set; } = null!;
    public Project Project { get; set; } = null!;
    public AnalysisReport? LastSbomReport { get; set; }
    public List<AnalysisVulnerabilityObservation> VulnerabilityObservations { get; set; } = [];
}
