// SPDX-License-Identifier: EUPL-1.2
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Aetheus.Back.Components.Analysis;

internal static class AnalysisTelemetry
{
    private static readonly Meter s_meter = new("Aetheus.Analysis.Back");
    private static readonly Histogram<double> s_queueDuration = s_meter.CreateHistogram<double>(
        "aetheus.analysis.ingest.queue.duration", "s", "Time waiting for analysis ingestion capacity.");
    private static readonly Histogram<double> s_ingestDuration = s_meter.CreateHistogram<double>(
        "aetheus.analysis.ingest.duration", "s", "Analysis report ingestion duration.");
    private static readonly Histogram<long> s_reportSize = s_meter.CreateHistogram<long>(
        "aetheus.analysis.report.size", "By", "Analysis report payload size.");
    private static readonly Counter<long> s_reports = s_meter.CreateCounter<long>(
        "aetheus.analysis.reports", "{report}", "Analysis reports ingested.");
    private static readonly Counter<long> s_findings = s_meter.CreateCounter<long>(
        "aetheus.analysis.findings", "{finding}", "Findings observed in ingested reports.");
    private static readonly Counter<long> s_failures = s_meter.CreateCounter<long>(
        "aetheus.analysis.failures", "{failure}", "Analysis ingestion or gate failures.");

    public static void RecordQueue(TimeSpan duration) => s_queueDuration.Record(duration.TotalSeconds);

    public static void RecordReport(
        TimeSpan duration,
        long contentSize,
        int findingCount,
        AnalysisReportStatus status,
        AnalysisGateStatus gateStatus)
    {
        var tags = new TagList
        {
            { "analysis.report.status", status.ToString() },
            { "analysis.gate.status", gateStatus.ToString() }
        };
        s_ingestDuration.Record(duration.TotalSeconds, tags);
        s_reportSize.Record(contentSize, tags);
        s_reports.Add(1, tags);
        s_findings.Add(findingCount, tags);
        if (status is AnalysisReportStatus.Error or AnalysisReportStatus.TimedOut or AnalysisReportStatus.Unavailable
            || gateStatus is AnalysisGateStatus.Error) s_failures.Add(1, tags);
    }
}
