// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Projects;

namespace Aetheus.Back.Components.Analysis;

internal sealed class AnalysisProjectSummaryRepository(AppDbContext db)
{
    public async Task<AnalysisProjectSummaryDto> GetAsync(
        int projectId,
        CancellationToken ct)
    {
        // One bounded SQL projection replaces the former nine sequential aggregate commands.
        // Scalar subqueries keep the result defined for projects with no reports/findings.
        var aggregate = await db.Projects.AsNoTracking()
            .Where(project => project.Id == projectId)
            .Select(project => new
            {
                OpenCount = db.AnalysisFindings.Count(finding =>
                    finding.ProjectId == projectId && finding.Status == AnalysisFindingStatus.Open),
                CriticalCount = db.AnalysisFindings.Count(finding =>
                    finding.ProjectId == projectId
                    && finding.Status == AnalysisFindingStatus.Open
                    && finding.Severity == AnalysisSeverity.Critical),
                HighCount = db.AnalysisFindings.Count(finding =>
                    finding.ProjectId == projectId
                    && finding.Status == AnalysisFindingStatus.Open
                    && finding.Severity == AnalysisSeverity.High),
                MediumCount = db.AnalysisFindings.Count(finding =>
                    finding.ProjectId == projectId
                    && finding.Status == AnalysisFindingStatus.Open
                    && finding.Severity == AnalysisSeverity.Medium),
                LowCount = db.AnalysisFindings.Count(finding =>
                    finding.ProjectId == projectId
                    && finding.Status == AnalysisFindingStatus.Open
                    && finding.Severity == AnalysisSeverity.Low),
                AcceptedCount = db.AnalysisFindings.Count(finding =>
                    finding.ProjectId == projectId && finding.Status == AnalysisFindingStatus.Accepted),
                FixedCount = db.AnalysisFindings.Count(finding =>
                    finding.ProjectId == projectId && finding.Status == AnalysisFindingStatus.Fixed),
                Latest = db.AnalysisReports
                    .Where(report => report.ProjectId == projectId)
                    .Max(report => (DateTime?)report.CompletedAt),
                NewCount = db.AnalysisFindingOccurrences
                    .Where(occurrence =>
                        occurrence.AnalysisReport.ProjectId == projectId
                        && occurrence.AnalysisReport.BranchName == project.DefaultBranch
                        && occurrence.IsNew
                        && occurrence.AnalysisReportId == db.AnalysisReports
                            .Where(report => report.ProjectId == projectId
                                && report.BranchName == project.DefaultBranch
                                && report.ScannerKey == occurrence.ScannerKey
                                && report.Category == occurrence.AnalysisReport.Category
                                && (report.Status == AnalysisReportStatus.Passed
                                    || report.Status == AnalysisReportStatus.Failed))
                            .OrderByDescending(report => report.CompletedAt)
                            .ThenByDescending(report => report.Id)
                            .Select(report => report.Id)
                            .FirstOrDefault())
                    .Select(occurrence => occurrence.AnalysisFindingId)
                    .Distinct()
                    .Count()
            })
            .SingleOrDefaultAsync(ct)
            .ConfigureAwait(false);
        var grades = await GetGradesAsync([projectId], ct).ConfigureAwait(false);

        return new AnalysisProjectSummaryDto
        {
            ProjectId = projectId,
            OpenCount = aggregate?.OpenCount ?? 0,
            NewCount = aggregate?.NewCount ?? 0,
            CriticalCount = aggregate?.CriticalCount ?? 0,
            HighCount = aggregate?.HighCount ?? 0,
            MediumCount = aggregate?.MediumCount ?? 0,
            LowCount = aggregate?.LowCount ?? 0,
            AcceptedCount = aggregate?.AcceptedCount ?? 0,
            FixedCount = aggregate?.FixedCount ?? 0,
            LastAnalysisAt = aggregate?.Latest,
            Grade = grades.GetValueOrDefault(projectId)
        };
    }

    internal async Task<Dictionary<int, AnalysisGradeSummaryDto>> GetGradesAsync(
        IReadOnlyCollection<int> projectIds,
        CancellationToken ct)
    {
        if (projectIds.Count == 0) return [];

        var latestRuns = await db.AnalysisEvaluations.AsNoTracking()
            .Where(evaluation => projectIds.Contains(evaluation.ProjectId)
                && evaluation.PipelineRunId.HasValue
                && evaluation.GradeSnapshotJson != "{}")
            .GroupBy(evaluation => new
            {
                evaluation.ProjectId,
                RunId = evaluation.PipelineRunId!.Value
            })
            .Select(group => new
            {
                group.Key.ProjectId,
                group.Key.RunId,
                EvaluatedAt = group.Max(evaluation => evaluation.EvaluatedAt)
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);
        if (latestRuns.Count == 0) return [];

        var selectedRuns = latestRuns
            .GroupBy(run => run.ProjectId)
            .SelectMany(group => group
                .OrderByDescending(run => run.EvaluatedAt)
                .Take(20))
            .ToList();
        var runIds = selectedRuns.Select(run => run.RunId).ToArray();
        var rows = await db.AnalysisEvaluations.AsNoTracking()
            .Where(evaluation => evaluation.PipelineRunId.HasValue
                && runIds.Contains(evaluation.PipelineRunId.Value)
                && evaluation.GradeSnapshotJson != "{}")
            .Select(evaluation => new
            {
                evaluation.ProjectId,
                RunId = evaluation.PipelineRunId!.Value,
                evaluation.GradeSnapshotJson
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return rows
            .GroupBy(row => row.ProjectId)
            .Select(projectGroup => new
            {
                ProjectId = projectGroup.Key,
                Summaries = projectGroup
                    .GroupBy(row => row.RunId)
                    .Select(runGroup => AnalysisGradeEngine.Aggregate(
                        runGroup.Select(row => row.GradeSnapshotJson),
                        pipelineRunId: runGroup.Key))
                    .Where(summary => summary is not null)
                    .Cast<AnalysisGradeSummaryDto>()
                    .ToList()
            })
            .Where(project => project.Summaries.Count > 0)
            .ToDictionary(
                project => project.ProjectId,
                project => AnalysisGradeEngine.CombineLatestDomains(project.Summaries));
    }
}

internal sealed class ProjectAnalysisGradeRepository(AppDbContext db) : IProjectAnalysisGradeReader
{
    private readonly AnalysisProjectSummaryRepository _summary = new(db);

    public Task<Dictionary<int, AnalysisGradeSummaryDto>> GetGradesAsync(
        IReadOnlyCollection<int> projectIds,
        CancellationToken ct = default) =>
        _summary.GetGradesAsync(projectIds, ct);
}
