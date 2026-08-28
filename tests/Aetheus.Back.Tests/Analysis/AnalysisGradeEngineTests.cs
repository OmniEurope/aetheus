// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Analysis;
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;

namespace Aetheus.Back.Tests.Analysis;

public sealed class AnalysisGradeEngineTests
{
    private static readonly DateTime EvaluatedAt =
        new(2026, 7, 27, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Evaluate_CoverageAtSeventyFivePercent_IsGradeA()
    {
        var report = Report(
            AnalysisCategory.Coverage,
            Metric("coverage.line.percent", 75, "percent"));

        var result = AnalysisGradeEngine.Evaluate(report, Context(CoverageYaml), EvaluatedAt);
        var summary = AnalysisGradeEngine.Aggregate([result.SnapshotJson]);

        Assert.Equal(AnalysisGrade.A, result.Grade);
        Assert.Equal(AnalysisGrade.A, summary!.OverallGrade);
        Assert.Equal(AnalysisGradeCompleteness.Complete, summary.Completeness);
    }

    [Fact]
    public void Aggregate_UsesWorstRequiredDomainWithoutAWeightedAverage()
    {
        var coverage = AnalysisGradeEngine.Evaluate(
            Report(AnalysisCategory.Coverage, Metric("coverage.line.percent", 80, "percent")),
            Context(QualityYaml),
            EvaluatedAt);
        var duplication = AnalysisGradeEngine.Evaluate(
            Report(AnalysisCategory.Duplication, Metric("duplication.percentage", 4, "percent")),
            Context(QualityYaml),
            EvaluatedAt);
        var architecture = AnalysisGradeEngine.Evaluate(
            Report(AnalysisCategory.Architecture, Metric("architecture.cycles", 2, "cycles")),
            Context(QualityYaml),
            EvaluatedAt);

        var summary = AnalysisGradeEngine.Aggregate(
            [coverage.SnapshotJson, duplication.SnapshotJson, architecture.SnapshotJson]);

        Assert.Equal(AnalysisGrade.C, summary!.OverallGrade);
        Assert.Equal(AnalysisGradeDomain.Architecture, summary.LimitingDomain);
    }

    [Fact]
    public void Aggregate_ArchitectureMetricsRemainCompleteWhenFindingsReportHasNoMetric()
    {
        var metrics = AnalysisGradeEngine.Evaluate(
            Report(AnalysisCategory.Architecture, Metric("architecture.cycles", 0, "cycles")),
            Context(QualityYaml),
            EvaluatedAt);
        var findings = AnalysisGradeEngine.Evaluate(
            new AnalysisReport
            {
                Id = 5,
                Category = AnalysisCategory.Architecture,
                Status = AnalysisReportStatus.Passed
            },
            Context(QualityYaml),
            EvaluatedAt.AddSeconds(1));

        var summary = AnalysisGradeEngine.Aggregate(
            [metrics.SnapshotJson, findings.SnapshotJson]);
        var architecture = Assert.Single(summary!.Domains, domain =>
            domain.Domain == AnalysisGradeDomain.Architecture);

        Assert.Equal(AnalysisGrade.A, architecture.Grade);
        Assert.Equal(AnalysisGradeCompleteness.Complete, architecture.Completeness);
    }

    [Fact]
    public void Evaluate_CriticalFinding_ForcesGradeF()
    {
        var finding = new AnalysisFinding
        {
            Fingerprint = "critical-fingerprint",
            Severity = AnalysisSeverity.Critical,
            Category = AnalysisCategory.Sast
        };
        var report = new AnalysisReport
        {
            Id = 4,
            Category = AnalysisCategory.Sast,
            Status = AnalysisReportStatus.Passed,
            Occurrences =
            {
                new AnalysisFindingOccurrence
                {
                    IsNew = true,
                    AnalysisFinding = finding
                }
            }
        };

        var result = AnalysisGradeEngine.Evaluate(report, Context(SecurityYaml), EvaluatedAt);

        Assert.Equal(AnalysisGrade.F, result.Grade);
    }

    [Fact]
    public void Aggregate_DoesNotTurnMissingRequiredEvidenceIntoGradeF()
    {
        var report = Report(
            AnalysisCategory.Duplication,
            Metric("duplication.percentage", 2, "percent"));

        var result = AnalysisGradeEngine.Evaluate(report, Context(QualityYaml), EvaluatedAt);
        var summary = AnalysisGradeEngine.Aggregate([result.SnapshotJson]);

        Assert.Null(summary!.OverallGrade);
        Assert.Equal(AnalysisGradeCompleteness.Incomplete, summary.Completeness);
    }

    [Fact]
    public void Evaluate_MissingApplicableRequiredMeasure_IsIncompleteWithoutGrade()
    {
        var report = Report(
            AnalysisCategory.Coverage,
            Metric("coverage.line.percent", 80, "percent"));

        var result = AnalysisGradeEngine.Evaluate(
            report,
            Context(CoverageWithMissingRequiredMetricYaml),
            EvaluatedAt);

        Assert.Null(result.Grade);
        Assert.Equal(AnalysisGradeCompleteness.Incomplete, result.Completeness);
    }

    [Fact]
    public void Evaluate_ProducesDeterministicSnapshotHash()
    {
        var report = Report(
            AnalysisCategory.Coverage,
            Metric("coverage.line.percent", 75, "percent"));

        var first = AnalysisGradeEngine.Evaluate(report, Context(CoverageYaml), EvaluatedAt);
        var second = AnalysisGradeEngine.Evaluate(report, Context(CoverageYaml), EvaluatedAt);

        Assert.Equal(first.SnapshotJson, second.SnapshotJson);
        Assert.Equal(first.SnapshotHash, second.SnapshotHash);
        Assert.Equal(64, first.SnapshotHash.Length);
    }

    [Fact]
    public void CombineLatestDomains_PreservesOlderRequiredDomainsFromDifferentRuns()
    {
        var quality = Summary(
            AnalysisGradeDomain.Reliability,
            AnalysisGrade.B,
            EvaluatedAt.AddMinutes(-5));
        var security = Summary(
            AnalysisGradeDomain.Security,
            AnalysisGrade.C,
            EvaluatedAt);

        var summary = AnalysisGradeEngine.CombineLatestDomains([security, quality]);

        Assert.Equal(AnalysisGrade.C, summary.OverallGrade);
        Assert.Equal(AnalysisGradeDomain.Security, summary.LimitingDomain);
        Assert.Equal(AnalysisGradeCompleteness.Complete, summary.Completeness);
        Assert.Contains(summary.Domains, domain =>
            domain.Domain == AnalysisGradeDomain.Reliability
            && domain.Grade == AnalysisGrade.B);
        Assert.Contains(summary.Domains, domain =>
            domain.Domain == AnalysisGradeDomain.Security
            && domain.Grade == AnalysisGrade.C);
    }

    private static AnalysisReport Report(AnalysisCategory category, AnalysisMetric metric) => new()
    {
        Id = 4,
        Category = category,
        Status = AnalysisReportStatus.Passed,
        Metrics = { metric }
    };

    private static AnalysisMetric Metric(string key, double value, string unit) => new()
    {
        Key = key,
        Value = value,
        Unit = unit,
        ToolName = "test"
    };

    private static AnalysisRunContext Context(string yaml) =>
        new(1, 2, 3, "main", new string('a', 40), "main", PipelineYaml: yaml);

    private static AnalysisGradeSummaryDto Summary(
        AnalysisGradeDomain domain,
        AnalysisGrade grade,
        DateTime evaluatedAt) => new()
        {
            OverallGrade = grade,
            MinimumGrade = AnalysisGrade.C,
            Completeness = AnalysisGradeCompleteness.Complete,
            LimitingDomain = domain,
            CommitHash = new string('a', 40),
            EvaluatedAt = evaluatedAt,
            Domains =
        [
            new AnalysisGradeDomainDto
            {
                Domain = domain,
                Grade = grade,
                Required = true,
                Completeness = AnalysisGradeCompleteness.Complete,
                CommitHash = new string('a', 40),
                EvaluatedAt = evaluatedAt,
                Measures =
                [
                    new AnalysisGradeMeasureDto
                    {
                        Key = $"grade.{domain}",
                        Domain = domain,
                        Grade = grade,
                        Required = true,
                        Observed = true,
                        ObservedValue = 1
                    }
                ]
            }
        ]
        };

    private const string QualityYaml = """
        stages:
          - name: Quality
            steps:
              - name: Grade
                type: analysis-gate
                analysis_scope: quality
                analysis_grading:
                  version: 1
                  minimum_grade: C
                  required_domains: [reliability, code-quality, architecture]
                  rules:
                    - key: grade.coverage
                      domain: reliability
                      metric: coverage.line.percent
                      category: coverage
                      direction: higher-is-better
                      a: 75
                      b: 65
                      c: 55
                      d: 40
                      e: 20
                    - key: grade.duplication
                      domain: code-quality
                      metric: duplication.percentage
                      category: duplication
                      direction: lower-is-better
                      a: 3
                      b: 5
                      c: 8
                      d: 12
                      e: 20
                    - key: grade.architecture
                      domain: architecture
                      metric: architecture.cycles
                      category: architecture
                      direction: lower-is-better
                      a: 0
                      b: 1
                      c: 2
                      d: 4
                      e: 8
        """;

    private const string CoverageYaml = """
        stages:
          - name: Quality
            steps:
              - name: Grade
                type: analysis-gate
                analysis_scope: quality
                analysis_grading:
                  version: 1
                  minimum_grade: C
                  required_domains: [reliability]
                  rules:
                    - key: grade.coverage
                      domain: reliability
                      metric: coverage.line.percent
                      category: coverage
                      direction: higher-is-better
                      a: 75
                      b: 65
                      c: 55
                      d: 40
                      e: 20
        """;

    private const string CoverageWithMissingRequiredMetricYaml = """
        stages:
          - name: Quality
            steps:
              - name: Grade
                type: analysis-gate
                analysis_scope: quality
                analysis_grading:
                  version: 1
                  minimum_grade: C
                  required_domains: [reliability]
                  rules:
                    - key: grade.coverage.line
                      domain: reliability
                      metric: coverage.line.percent
                      category: coverage
                      direction: higher-is-better
                      a: 75
                      b: 65
                      c: 55
                      d: 40
                      e: 20
                    - key: grade.coverage.branch
                      domain: reliability
                      metric: coverage.branch.percent
                      category: coverage
                      direction: higher-is-better
                      a: 75
                      b: 65
                      c: 55
                      d: 40
                      e: 20
        """;

    private const string SecurityYaml = """
        stages:
          - name: Security
            steps:
              - name: Grade
                type: analysis-gate
                analysis_scope: security
                analysis_grading:
                  version: 1
                  minimum_grade: C
                  required_domains: [security]
                  rules:
                    - key: grade.security.critical
                      domain: security
                      severity: critical
                      direction: lower-is-better
                      a: 0
                      b: 0
                      c: 0
                      d: 0
                      e: 0
        """;
}
