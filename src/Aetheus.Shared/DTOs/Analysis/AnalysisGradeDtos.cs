// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;
using Aetheus.Shared.Enums;

namespace Aetheus.Shared.DTOs;

public sealed record AnalysisGradeMeasureDto
{
    [StringLength(200)] public string Key { get; init; } = string.Empty;
    public AnalysisGradeDomain Domain { get; init; }
    public AnalysisGrade? Grade { get; init; }
    public bool Required { get; init; }
    public bool Observed { get; init; }
    public double? ObservedValue { get; init; }
    [StringLength(300)] public string? MetricKey { get; init; }
    public AnalysisCategory? Category { get; init; }
    public AnalysisSeverity? Severity { get; init; }
    [StringLength(50)] public string? Unit { get; init; }
    public AnalysisMetricDirection Direction { get; init; }
    public double? AThreshold { get; init; }
    public double? BThreshold { get; init; }
    public double? CThreshold { get; init; }
    public double? DThreshold { get; init; }
    public double? EThreshold { get; init; }
    public AnalysisGrade? NextGrade { get; init; }
    public double? DistanceToNextGrade { get; init; }
}

public sealed record AnalysisGradeDomainDto
{
    public AnalysisGradeDomain Domain { get; init; }
    public AnalysisGrade? Grade { get; init; }
    public bool Required { get; init; }
    public AnalysisGradeCompleteness Completeness { get; init; }
    [StringLength(64)] public string? CommitHash { get; init; }
    public DateTime? EvaluatedAt { get; init; }
    public IReadOnlyList<AnalysisGradeMeasureDto> Measures { get; init; } = [];
}

public sealed record AnalysisGradeSummaryDto
{
    public int SchemaVersion { get; init; } = 1;
    public AnalysisGrade? OverallGrade { get; init; }
    public AnalysisGrade? MinimumGrade { get; init; }
    public AnalysisGradeCompleteness Completeness { get; init; }
    public AnalysisGradeDomain? LimitingDomain { get; init; }
    [StringLength(64)] public string SnapshotHash { get; init; } = string.Empty;
    [StringLength(64)] public string? CommitHash { get; init; }
    public int? PipelineRunId { get; init; }
    public DateTime? EvaluatedAt { get; init; }
    public IReadOnlyList<AnalysisGradeDomainDto> Domains { get; init; } = [];
}
