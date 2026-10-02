// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Analysis;

/// <summary>Read-only collaborator of <see cref="AnalysisRepository"/> for the findings grid.</summary>
internal sealed class AnalysisFindingOccurrenceRepository(AppDbContext db)
{
    /// <summary>
    /// R-460: the latest occurrence of each finding of a page, with only the columns the grid maps. The
    /// former <c>Include</c> of the report, its run and its pipeline loaded, for every row, the full YAML
    /// and JSON documents those rows carry; the page needs two identifiers from them. The instances are
    /// untracked projections: only the members set below are filled.
    /// </summary>
    public async Task<List<AnalysisFindingOccurrence>> GetLatestAsync(List<int> findingIds, CancellationToken ct)
    {
        var latestIds = await db.AnalysisFindingOccurrences.AsNoTracking()
            .Where(occurrence => findingIds.Contains(occurrence.AnalysisFindingId))
            .GroupBy(occurrence => occurrence.AnalysisFindingId)
            .Select(group => group.OrderByDescending(occurrence => occurrence.CreatedAt)
                .ThenByDescending(occurrence => occurrence.Id)
                .Select(occurrence => occurrence.Id)
                .First())
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return await db.AnalysisFindingOccurrences.AsNoTracking()
            .Where(occurrence => latestIds.Contains(occurrence.Id))
            .Select(occurrence => new AnalysisFindingOccurrence
            {
                Id = occurrence.Id,
                AnalysisReportId = occurrence.AnalysisReportId,
                AnalysisFindingId = occurrence.AnalysisFindingId,
                ToolName = occurrence.ToolName,
                ScannerKey = occurrence.ScannerKey,
                RuleId = occurrence.RuleId,
                FilePath = occurrence.FilePath,
                StartLine = occurrence.StartLine,
                EndLine = occurrence.EndLine,
                Symbol = occurrence.Symbol,
                Message = occurrence.Message,
                BranchName = occurrence.BranchName,
                CommitHash = occurrence.CommitHash,
                IsNew = occurrence.IsNew,
                CreatedAt = occurrence.CreatedAt,
                AnalysisReport = new AnalysisReport
                {
                    Id = occurrence.AnalysisReportId,
                    PipelineRunId = occurrence.AnalysisReport.PipelineRunId,
                    PipelineRun = occurrence.AnalysisReport.PipelineRun == null
                        ? null
                        : new PipelineRun
                        {
                            Id = occurrence.AnalysisReport.PipelineRun.Id,
                            PipelineId = occurrence.AnalysisReport.PipelineRun.PipelineId,
                            Pipeline = new Pipeline
                            {
                                Id = occurrence.AnalysisReport.PipelineRun.PipelineId,
                                SourceRepositoryId = occurrence.AnalysisReport.PipelineRun.Pipeline.SourceRepositoryId
                            }
                        }
                }
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }
}
