// SPDX-License-Identifier: EUPL-1.2
using System.IO.Enumeration;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Analysis;

internal static class AnalysisGradeEngine
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static AnalysisGradeEvaluation Evaluate(
        AnalysisReport report,
        AnalysisRunContext context,
        DateTime now)
    {
        var grading = Resolve(context.PipelineYaml, report.Category);
        if (grading is null)
            return AnalysisGradeEvaluation.Empty;

        PipelineAnalysisGradingValidator.TryParseGrade(grading.MinimumGrade, out var minimumGrade);
        var requiredDomains = grading.RequiredDomains
            .Select(value => PipelineAnalysisGradingValidator.TryParseDomain(value, out var domain)
                ? domain
                : null)
            .Where(domain => domain.HasValue)
            .Select(domain => domain!.Value)
            .ToHashSet();

        var measures = grading.Rules
            .OrderBy(rule => rule.Key, StringComparer.OrdinalIgnoreCase)
            .Select(rule => EvaluateRule(
                rule,
                report,
                report.Status is AnalysisReportStatus.Passed or AnalysisReportStatus.Failed))
            .ToList();
        var summary = BuildSummary(
            measures,
            requiredDomains,
            minimumGrade,
            context.CommitHash,
            context.PipelineRunId,
            now);
        var json = Serialize(summary);
        var applicableRequiredMeasures = measures
            .Where(measure => measure.Required
                && (!measure.Category.HasValue || measure.Category.Value == report.Category))
            .ToList();
        var complete = applicableRequiredMeasures.Count > 0
            && applicableRequiredMeasures.All(measure => measure.Observed && measure.Grade.HasValue);
        return new AnalysisGradeEvaluation(
            complete ? applicableRequiredMeasures.Max(measure => measure.Grade) : null,
            complete ? AnalysisGradeCompleteness.Complete : AnalysisGradeCompleteness.Incomplete,
            json,
            Hash(json));
    }

    public static AnalysisGradeSummaryDto? Aggregate(
        IEnumerable<string> snapshots,
        string? commitHash = null,
        int? pipelineRunId = null)
    {
        var summaries = snapshots
            .Where(snapshot => !string.IsNullOrWhiteSpace(snapshot) && snapshot != "{}")
            .Select(Deserialize)
            .Where(summary => summary is not null)
            .Cast<AnalysisGradeSummaryDto>()
            .ToList();
        if (summaries.Count == 0) return null;

        var requiredDomains = summaries
            .SelectMany(summary => summary.Domains)
            .Where(domain => domain.Required)
            .Select(domain => domain.Domain)
            .ToHashSet();
        var minimumGrade = summaries
            .Where(summary => summary.MinimumGrade.HasValue)
            .Select(summary => summary.MinimumGrade!.Value)
            .Cast<AnalysisGrade?>()
            .OrderBy(grade => grade)
            .FirstOrDefault();
        var measures = summaries
            .SelectMany(summary => summary.Domains)
            .SelectMany(domain => domain.Measures)
            .GroupBy(measure => measure.Key, StringComparer.OrdinalIgnoreCase)
            .Select(AggregateMeasure)
            .OrderBy(measure => measure.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var commits = summaries
            .Select(summary => summary.CommitHash)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var summaryCommit = commitHash ?? (commits.Count == 1 ? commits[0] : null);
        return WithHash(BuildSummary(
            measures,
            requiredDomains,
            minimumGrade,
            summaryCommit,
            pipelineRunId ?? summaries.Select(summary => summary.PipelineRunId).Distinct().SingleOrDefault(),
            summaries.Max(summary => summary.EvaluatedAt)));
    }

    private static PipelineAnalysisGradingDefinition? Resolve(
        string yaml,
        AnalysisCategory category)
    {
        var definition = YamlParsingHelper.ParseAndValidate(yaml);
        if (definition is null) return null;
        var scope = AnalysisGateScopes.ForCategory(category);
        var grading = YamlParsingHelper.FlattenJobs(definition)
            .SelectMany(stage => stage.Steps)
            .FirstOrDefault(step =>
                string.Equals(step.Type, "analysis-gate", StringComparison.OrdinalIgnoreCase)
                && AnalysisGateScopes.Normalize(step.AnalysisScope) == scope)
            ?.AnalysisGrading;
        return PipelineAnalysisGradingValidator.Validate(scope, grading).Count == 0
            ? grading
            : null;
    }

    private static AnalysisGradeMeasureDto EvaluateRule(
        PipelineAnalysisGradeRuleDefinition rule,
        AnalysisReport report,
        bool evidenceAvailable)
    {
        PipelineAnalysisGradingValidator.TryParseDomain(rule.Domain, out var domain);
        PipelineAnalysisGradingValidator.TryParseDirection(rule.Direction, out var direction);
        PipelineAnalysisGateRuleValidator.TryParseEnum<AnalysisCategory>(rule.Category, out var category);
        PipelineAnalysisGateRuleValidator.TryParseEnum<AnalysisSeverity>(rule.Severity, out var severity);
        var applies = evidenceAvailable
            && (!category.HasValue || category.Value == report.Category);
        var values = new List<double>();
        string? unit = null;

        if (applies && !string.IsNullOrWhiteSpace(rule.Metric))
        {
            var metrics = report.Metrics
                .Where(metric => FileSystemName.MatchesSimpleExpression(
                    rule.Metric,
                    metric.Key,
                    true))
                .ToList();
            values.AddRange(metrics.Select(metric => metric.Value));
            unit = metrics.Select(metric => metric.Unit).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        }
        else if (applies && severity.HasValue)
        {
            values.Add(report.Occurrences
                .Where(occurrence => occurrence.AnalysisFinding.Severity == severity.Value
                    && (!rule.NewFindingsOnly || occurrence.IsNew))
                .Select(occurrence => occurrence.AnalysisFinding.Fingerprint)
                .Distinct(StringComparer.Ordinal)
                .Count());
        }

        var observed = values.Count > 0;
        double? observedValue = observed
            ? severity.HasValue
                ? values.Sum()
                : direction == AnalysisMetricDirection.HigherIsBetter
                    ? values.Min()
                    : values.Max()
            : null;
        return BuildMeasure(rule, domain!.Value, direction!.Value, category, severity, observedValue, unit);
    }

    private static AnalysisGradeMeasureDto AggregateMeasure(
        IGrouping<string, AnalysisGradeMeasureDto> group)
    {
        var first = group.First();
        var values = group
            .Where(measure => measure.Observed && measure.ObservedValue.HasValue)
            .Select(measure => measure.ObservedValue!.Value)
            .ToList();
        double? value = values.Count == 0
            ? null
            : first.Severity.HasValue
                ? values.Sum()
                : first.Direction == AnalysisMetricDirection.HigherIsBetter
                    ? values.Min()
                    : values.Max();
        return BuildMeasure(first, value);
    }

    private static AnalysisGradeMeasureDto BuildMeasure(
        PipelineAnalysisGradeRuleDefinition rule,
        AnalysisGradeDomain domain,
        AnalysisMetricDirection direction,
        AnalysisCategory? category,
        AnalysisSeverity? severity,
        double? observedValue,
        string? unit) =>
        BuildMeasure(new AnalysisGradeMeasureDto
        {
            Key = rule.Key,
            Domain = domain,
            Required = rule.Required,
            MetricKey = rule.Metric,
            Category = category,
            Severity = severity,
            Unit = unit,
            Direction = direction,
            AThreshold = rule.A,
            BThreshold = rule.B,
            CThreshold = rule.C,
            DThreshold = rule.D,
            EThreshold = rule.E
        }, observedValue);

    private static AnalysisGradeMeasureDto BuildMeasure(
        AnalysisGradeMeasureDto source,
        double? observedValue)
    {
        AnalysisGrade? grade = observedValue.HasValue ? Grade(source, observedValue.Value) : null;
        AnalysisGrade? nextGrade = grade is > AnalysisGrade.A
            ? (AnalysisGrade)((int)grade.Value - 1)
            : null;
        var nextThreshold = nextGrade switch
        {
            AnalysisGrade.A => source.AThreshold,
            AnalysisGrade.B => source.BThreshold,
            AnalysisGrade.C => source.CThreshold,
            AnalysisGrade.D => source.DThreshold,
            AnalysisGrade.E => source.EThreshold,
            _ => null
        };
        double? distance = observedValue.HasValue && nextThreshold.HasValue
            ? source.Direction == AnalysisMetricDirection.HigherIsBetter
                ? Math.Max(0, nextThreshold.Value - observedValue.Value)
                : Math.Max(0, observedValue.Value - nextThreshold.Value)
            : null;
        return source with
        {
            Observed = observedValue.HasValue,
            ObservedValue = observedValue,
            Grade = grade,
            NextGrade = nextGrade,
            DistanceToNextGrade = distance
        };
    }

    private static AnalysisGrade Grade(AnalysisGradeMeasureDto measure, double value)
    {
        var thresholds = new[]
        {
            measure.AThreshold!.Value,
            measure.BThreshold!.Value,
            measure.CThreshold!.Value,
            measure.DThreshold!.Value,
            measure.EThreshold!.Value
        };
        for (var index = 0; index < thresholds.Length; index++)
        {
            var matches = measure.Direction == AnalysisMetricDirection.HigherIsBetter
                ? value >= thresholds[index]
                : value <= thresholds[index];
            if (matches) return (AnalysisGrade)index;
        }
        return AnalysisGrade.F;
    }

    private static AnalysisGradeSummaryDto BuildSummary(
        IReadOnlyCollection<AnalysisGradeMeasureDto> measures,
        IReadOnlySet<AnalysisGradeDomain> requiredDomains,
        AnalysisGrade? minimumGrade,
        string? commitHash,
        int? pipelineRunId,
        DateTime? evaluatedAt)
    {
        var domains = Enum.GetValues<AnalysisGradeDomain>()
            .Select(domain =>
            {
                var domainMeasures = measures.Where(measure => measure.Domain == domain).ToList();
                var gradingMeasures = domainMeasures.Where(measure => measure.Required).ToList();
                var required = requiredDomains.Contains(domain);
                var complete = gradingMeasures.Count > 0
                    && gradingMeasures.All(measure => measure.Observed && measure.Grade.HasValue);
                return new AnalysisGradeDomainDto
                {
                    Domain = domain,
                    Grade = complete ? gradingMeasures.Max(measure => measure.Grade) : null,
                    Required = required,
                    Completeness = complete
                        ? AnalysisGradeCompleteness.Complete
                        : AnalysisGradeCompleteness.Incomplete,
                    CommitHash = commitHash,
                    EvaluatedAt = evaluatedAt,
                    Measures = domainMeasures
                };
            })
            .ToList();
        var required = domains.Where(domain => domain.Required).ToList();
        var complete = required.Count > 0
            && required.All(domain => domain.Completeness == AnalysisGradeCompleteness.Complete
                && domain.Grade.HasValue);
        var grade = complete ? required.Max(domain => domain.Grade) : null;
        return new AnalysisGradeSummaryDto
        {
            OverallGrade = grade,
            MinimumGrade = minimumGrade,
            Completeness = complete
                ? AnalysisGradeCompleteness.Complete
                : AnalysisGradeCompleteness.Incomplete,
            LimitingDomain = grade.HasValue
                ? required.First(domain => domain.Grade == grade).Domain
                : null,
            CommitHash = commitHash,
            PipelineRunId = pipelineRunId,
            EvaluatedAt = evaluatedAt,
            Domains = domains
        };
    }

    private static AnalysisGradeSummaryDto WithHash(AnalysisGradeSummaryDto summary)
    {
        var json = Serialize(summary with { SnapshotHash = string.Empty });
        return summary with { SnapshotHash = Hash(json) };
    }

    private static string Serialize(AnalysisGradeSummaryDto summary) =>
        JsonSerializer.Serialize(summary with { SnapshotHash = string.Empty }, JsonOptions);

    private static AnalysisGradeSummaryDto? Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<AnalysisGradeSummaryDto>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Hash(string json) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
}

internal sealed record AnalysisGradeEvaluation(
    AnalysisGrade? Grade,
    AnalysisGradeCompleteness Completeness,
    string SnapshotJson,
    string SnapshotHash)
{
    public static AnalysisGradeEvaluation Empty { get; } =
        new(null, AnalysisGradeCompleteness.Incomplete, "{}", string.Empty);
}
