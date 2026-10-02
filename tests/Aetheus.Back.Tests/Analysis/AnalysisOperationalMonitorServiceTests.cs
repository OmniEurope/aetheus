// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Analysis;

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

    /// <summary>
    /// The probe says "unavailable" both for a scanner that is broken and for one this host will never
    /// be able to run. Only the first is an operator's problem: a Windows or arm64 runner cannot grow
    /// x64 Linux support, so alerting on it repeats every cooldown with nothing to do, which is how an
    /// alert channel stops being read. The capability stays reported on the server either way.
    /// </summary>
    [Fact]
    public void DetectIssues_AScannerTheHostCanNeverRun_IsNotAnOperationalAlert()
    {
        var now = new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero);
        var snapshot = new AnalysisOperationalSnapshot(
            [],
            [new AnalysisServerHealthRow(1, 20, "runner", """
                [
                  "scanner:opengrep:1.22.0:unavailable:verified binary supports Linux x64 only",
                  "scanner:trivy:0.60.0:unavailable:docker daemon or socket unavailable"
                ]
                """)],
            []);
        var options = new AnalysisPlatformOptions
        {
            ProjectDailyReportLimit = 1000,
            ProjectDailyBytesLimit = 2_000_000,
            StorageWarningPercent = 80,
            ScannerManifestMaxAgeDays = 45
        };
        var manifest = new ScannerManifest { SchemaVersion = 1, UpdatedAt = now.UtcDateTime };

        var issues = AnalysisOperationalMonitorService.DetectIssues(snapshot, manifest, options, now);

        // The broken one still alerts: this filters a platform fact, it does not silence the family.
        var alert = Assert.Single(issues, issue => issue.EventType == "analysis.operational.scanner-unavailable");
        Assert.Contains("docker daemon", alert.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(issues, issue => issue.Message.Contains("Linux x64 only", StringComparison.Ordinal));
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
