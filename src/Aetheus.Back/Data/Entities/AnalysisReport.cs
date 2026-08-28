// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Data.Entities;

public class AnalysisReport
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }
    public int ProjectId { get; set; }
    public int? PipelineRunId { get; set; }
    public int? PipelineArtifactId { get; set; }
    public string ScannerKey { get; set; } = string.Empty;
    public string ScannerName { get; set; } = string.Empty;
    public string ScannerVersion { get; set; } = string.Empty;
    public AnalysisCategory Category { get; set; }
    public AnalysisReportStatus Status { get; set; }
    public AnalysisReportFormat Format { get; set; }
    public string? ReportPath { get; set; }
    public string ContentHash { get; set; } = string.Empty;
    public string? PayloadHash { get; set; }
    public long ContentSize { get; set; }
    public string? BranchName { get; set; }
    public string? EnvironmentName { get; set; }
    public string? CommitHash { get; set; }
    public string? StageName { get; set; }
    public string? StepName { get; set; }
    public string? RuleSetHash { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime CompletedAt { get; set; }
    public bool IsTruncated { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }

    public Organization Organization { get; set; } = null!;
    public Project Project { get; set; } = null!;
    public PipelineRun? PipelineRun { get; set; }
    public PipelineArtifact? PipelineArtifact { get; set; }
    public List<AnalysisFindingOccurrence> Occurrences { get; set; } = [];
    public List<AnalysisMetric> Metrics { get; set; } = [];
    public List<AnalysisComponent> Components { get; set; } = [];
    public AnalysisEvaluation? Evaluation { get; set; }
}
