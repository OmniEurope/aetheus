// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Analysis;
using Aetheus.Shared.Analysis;

namespace Aetheus.Back.Tests.Analysis;

public sealed class AnalysisOperationalMonitorServiceTests
{
    [Fact]
    public void DetectIssues_ReportsAllRequiredOperationalFailureFamilies()
    {
        var now = new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero);
        var snapshot = new AnalysisOperationalSnapshot(
            [new AnalysisTrackingHealthRow(1, 10, "Failed", now.AddHours(-2).UtcDateTime, "provider unavailable")],
            [new AnalysisServerHealthRow(1, 20, "runner", """
                [
                  "scanner-cleanup:degraded:2 residual workspaces require operator review",
                  "scanner-db:trivy:degraded:database is 72 hours old",
                  "scanner:opengrep:1.22.0:unavailable:verified binary missing"
                ]
                """)],
            [new AnalysisStorageHealthRow(1, 10, 900, 1_800_000)]);
        var options = new AnalysisPlatformOptions
        {
            ProjectDailyReportLimit = 1000,
            ProjectDailyBytesLimit = 2_000_000,
            StorageWarningPercent = 80,
            ScannerManifestMaxAgeDays = 45
        };
        var manifest = new ScannerManifest { SchemaVersion = 1, UpdatedAt = now.AddDays(-60).UtcDateTime };

        var issues = AnalysisOperationalMonitorService.DetectIssues(snapshot, manifest, options, now);

        Assert.Contains(issues, issue => issue.EventType == "analysis.operational.scanner-obsolete");
        Assert.Contains(issues, issue => issue.EventType == "analysis.operational.cve-sync-failed");
        Assert.Contains(issues, issue => issue.EventType == "analysis.operational.cve-db-stale");
        Assert.Contains(issues, issue => issue.EventType == "analysis.operational.workspace-residual");
        Assert.Contains(issues, issue => issue.EventType == "analysis.operational.scanner-unavailable");
        Assert.Contains(issues, issue => issue.EventType == "analysis.operational.storage-drift");
    }

    [Fact]
    public void DetectIssues_HealthySnapshotProducesNoAlert()
    {
        var now = new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero);
        var snapshot = new AnalysisOperationalSnapshot(
            [new AnalysisTrackingHealthRow(1, 10, "Synchronized", now.AddMinutes(-10).UtcDateTime, null)],
            [new AnalysisServerHealthRow(1, 20, "runner", """
                ["scanner-cleanup:ready:0 residual workspaces", "scanner-db:trivy:ready:updated=2026-07-22T06:00:00Z"]
                """)],
            [new AnalysisStorageHealthRow(1, 10, 10, 10_000)]);
        var manifest = new ScannerManifest { SchemaVersion = 1, UpdatedAt = now.AddDays(-1).UtcDateTime };

        var issues = AnalysisOperationalMonitorService.DetectIssues(
            snapshot, manifest, new AnalysisPlatformOptions(), now);

        Assert.Empty(issues);
    }
}
