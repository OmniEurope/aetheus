// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Data.Entities;

public class DependencyTrackOutboxItem
{
    public int Id { get; set; }
    public int AnalysisReportId { get; set; }
    public int? PipelineArtifactId { get; set; }
    public int OrganizationId { get; set; }
    public int ProjectId { get; set; }
    public string ExternalProjectName { get; set; } = string.Empty;
    public string ProjectVersion { get; set; } = string.Empty;
    public string ReportEntryPath { get; set; } = string.Empty;
    public string Status { get; set; } = DependencyTrackOutboxStatuses.Pending;
    public int AttemptCount { get; set; }
    public DateTime NextAttemptAt { get; set; }
    public DateTime? LastAttemptAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public AnalysisReport AnalysisReport { get; set; } = null!;
    public PipelineArtifact? PipelineArtifact { get; set; }
    public Organization Organization { get; set; } = null!;
    public Project Project { get; set; } = null!;
}

public static class DependencyTrackOutboxStatuses
{
    public const string Pending = "Pending";
    public const string Retrying = "Retrying";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
}
