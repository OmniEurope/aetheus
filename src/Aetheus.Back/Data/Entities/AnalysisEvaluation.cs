// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Data.Entities;

public class AnalysisEvaluation
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }
    public int ProjectId { get; set; }
    public int AnalysisReportId { get; set; }
    public int? PipelineRunId { get; set; }
    public int? BaselineRunId { get; set; }
    public AnalysisGateStatus Status { get; set; }
    public string PolicySnapshotJson { get; set; } = "[]";
    public string PolicySnapshotHash { get; set; } = string.Empty;
    public AnalysisGrade? Grade { get; set; }
    public AnalysisGradeCompleteness GradeCompleteness { get; set; } = AnalysisGradeCompleteness.Incomplete;
    public string GradeSnapshotJson { get; set; } = "{}";
    public string GradeSnapshotHash { get; set; } = string.Empty;
    public int BlockerCount { get; set; }
    public int WarningCount { get; set; }
    public DateTime EvaluatedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public Organization Organization { get; set; } = null!;
    public Project Project { get; set; } = null!;
    public AnalysisReport AnalysisReport { get; set; } = null!;
    public PipelineRun? PipelineRun { get; set; }
    public PipelineRun? BaselineRun { get; set; }
}
