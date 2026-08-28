// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Analysis;

internal static class AnalysisReportQueryProjection
{
    public static IQueryable<AnalysisReportRow> SelectRows(this IQueryable<AnalysisReport> reports) =>
        reports.Select(report => new AnalysisReportRow(
            report,
            report.Occurrences.Select(occurrence => occurrence.AnalysisFindingId).Distinct().Count(),
            report.Occurrences.Count(occurrence => occurrence.IsNew),
            report.Components.Count,
            report.Metrics.Count,
            report.Evaluation == null ? null : report.Evaluation.Status,
            report.Evaluation == null ? null : report.Evaluation.Grade,
            report.Evaluation == null
                ? AnalysisGradeCompleteness.Incomplete
                : report.Evaluation.GradeCompleteness,
            report.Evaluation == null ? 0 : report.Evaluation.BlockerCount,
            report.Evaluation == null ? 0 : report.Evaluation.WarningCount));
}
