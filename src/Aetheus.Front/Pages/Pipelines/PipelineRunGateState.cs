// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Pipelines;

internal sealed class PipelineRunGateState
{
    public AnalysisRunGateDto? Result { get; private set; }
    public bool HasResult => Result is not null;
    public IReadOnlyList<int> FailedRunIds { get; private set; } = [];
    public bool IsIncomplete => FailedRunIds.Count > 0;

    public void Clear()
    {
        Result = null;
        FailedRunIds = [];
    }

    public async Task LoadAsync(
        ApiClient api,
        PipelineRunDto rootRun,
        IEnumerable<PipelineRunDto> childRuns,
        PipelineRunPageCache? pageCache = null,
        CancellationToken ct = default)
    {
        var runs = childRuns.Append(rootRun).DistinctBy(run => run.Id).ToList();
        var loads = await Task.WhenAll(runs.Select(run => LoadOneAsync(api, run, pageCache, ct)));
        FailedRunIds = loads.Where(load => load.Failed).Select(load => load.RunId).Order().ToList();
        Result = Merge(rootRun.Id, loads.Select(load => load.Gate).OfType<AnalysisRunGateDto>().Where(HasEvidence).ToList());
        if (Result is not null && IsIncomplete)
            Result = Result with { Status = AnalysisGateStatus.Error, Grade = null };
    }

    private static async Task<GateLoad> LoadOneAsync(
        ApiClient api,
        PipelineRunDto run,
        PipelineRunPageCache? pageCache,
        CancellationToken ct)
    {
        if (pageCache?.TryGetGate(run.Id, out var cached) == true)
            return new GateLoad(run.Id, cached, false);

        try
        {
            var gate = await api.Analysis.GetAnalysisRunGateAsync(run.Id, ct);
            if (gate is not null && HasEvidence(gate))
                pageCache?.StoreGate(run, gate);
            return new GateLoad(run.Id, gate, false);
        }
        catch (HttpRequestException) { return new GateLoad(run.Id, null, true); }
    }

    private readonly record struct GateLoad(int RunId, AnalysisRunGateDto? Gate, bool Failed);

    private static bool HasEvidence(AnalysisRunGateDto gate) =>
        gate.Reports.Count > 0
        || gate.Findings.Count > 0
        || gate.Violations.Count > 0
        || gate.MissingProducers.Count > 0
        || gate.Grade is not null;

    private static AnalysisRunGateDto? Merge(int rootRunId, IReadOnlyList<AnalysisRunGateDto> gates)
    {
        if (gates.Count == 0) return null;

        var findings = gates
            .SelectMany(gate => gate.Findings)
            .GroupBy(finding => finding.FindingId)
            .Select(group => group.First() with { IsNew = group.Any(finding => finding.IsNew) })
            .OrderByDescending(finding => finding.Severity)
            .ThenBy(finding => finding.Category)
            .ThenBy(finding => finding.FindingId)
            .ToList();
        var reports = gates
            .SelectMany(gate => gate.Reports)
            .DistinctBy(report => report.ReportId)
            .OrderBy(report => report.ReportId)
            .ToList();
        var grades = gates.Select(gate => gate.Grade).OfType<AnalysisGradeSummaryDto>().ToList();

        return new AnalysisRunGateDto
        {
            PipelineRunId = rootRunId,
            Status = gates.Max(gate => gate.Status),
            ReportCount = reports.Count,
            FindingCount = findings.Count,
            NewFindingCount = findings.Count(finding => finding.IsNew),
            ComponentCount = reports.Sum(report => report.ComponentCount),
            MetricCount = reports.Sum(report => report.MetricCount),
            BlockerCount = reports.Sum(report => report.BlockerCount),
            WarningCount = reports.Sum(report => report.WarningCount),
            Grade = grades.OrderByDescending(grade => grade.OverallGrade).FirstOrDefault(),
            MissingProducers = gates.SelectMany(gate => gate.MissingProducers).Distinct().ToList(),
            Reports = reports,
            Findings = findings,
            Violations = gates.SelectMany(gate => gate.Violations)
                .DistinctBy(violation => (violation.ReportId, violation.PolicyKey, violation.TargetKind, violation.TargetKey))
                .ToList()
        };
    }
}
