// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.Analysis;

internal sealed class AnalysisPortfolioProjectRepository(AppDbContext db)
{
    private readonly AnalysisProjectSummaryRepository _projectSummary = new(db);

    public async Task<List<AnalysisPortfolioProjectDto>> GetAsync(
        IReadOnlyCollection<int>? accessibleProjectIds,
        CancellationToken ct)
    {
        var query = db.Projects.AsNoTracking().AsQueryable();
        if (accessibleProjectIds is not null)
        {
            var projectIds = accessibleProjectIds.ToArray();
            query = query.Where(project => projectIds.Contains(project.Id));
        }

        var projects = await query
            .Select(project => new
            {
                project.Id,
                project.Name,
                project.OrganizationId,
                OrganizationName = project.Organization.Name,
                ReportCount = db.AnalysisReports.Count(report => report.ProjectId == project.Id),
                OpenCount = db.AnalysisFindings.Count(finding =>
                    finding.ProjectId == project.Id && finding.Status == AnalysisFindingStatus.Open),
                NewCount = db.AnalysisFindingOccurrences
                    .Where(occurrence => occurrence.AnalysisReport.ProjectId == project.Id && occurrence.IsNew)
                    .Select(occurrence => occurrence.AnalysisFindingId)
                    .Distinct()
                    .Count(),
                CriticalCount = db.AnalysisFindings.Count(finding =>
                    finding.ProjectId == project.Id
                    && finding.Status == AnalysisFindingStatus.Open
                    && finding.Severity == AnalysisSeverity.Critical),
                HighCount = db.AnalysisFindings.Count(finding =>
                    finding.ProjectId == project.Id
                    && finding.Status == AnalysisFindingStatus.Open
                    && finding.Severity == AnalysisSeverity.High),
                LastAnalysisAt = db.AnalysisReports
                    .Where(report => report.ProjectId == project.Id)
                    .Max(report => (DateTime?)report.CompletedAt)
            })
            .OrderBy(project => project.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var grades = await _projectSummary.GetGradesAsync(
            projects.Select(project => project.Id).ToArray(), ct).ConfigureAwait(false);
        return projects.Select(project => new AnalysisPortfolioProjectDto
        {
            ProjectId = project.Id,
            ProjectName = project.Name,
            OrganizationId = project.OrganizationId,
            OrganizationName = project.OrganizationName,
            ReportCount = project.ReportCount,
            OpenCount = project.OpenCount,
            NewCount = project.NewCount,
            CriticalCount = project.CriticalCount,
            HighCount = project.HighCount,
            LastAnalysisAt = project.LastAnalysisAt,
            Grade = grades.GetValueOrDefault(project.Id)
        }).ToList();
    }
}
