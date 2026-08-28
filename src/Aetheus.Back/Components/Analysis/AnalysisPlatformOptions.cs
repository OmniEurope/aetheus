// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Components.Analysis;

public sealed class AnalysisPlatformOptions
{
    public const string SectionName = "Analysis:Runtime";
    public int MaxConcurrentIngestions { get; set; } = 4;
    public int MaxConcurrentReportAdmissions { get; set; } = 1;
    public int ProjectDailyReportLimit { get; set; } = 1000;
    public long ProjectDailyBytesLimit { get; set; } = 2_147_483_648;
    public int RawReportRetentionDays { get; set; } = 30;
    public int FindingRetentionDays { get; set; } = 730;
    public int SbomRetentionDays { get; set; } = 365;
    public int LogRetentionDays { get; set; } = 30;
    public int EphemeralEnvironmentRetentionHours { get; set; } = 24;
    public int OperationalCheckIntervalMinutes { get; set; } = 60;
    public int ScannerManifestMaxAgeDays { get; set; } = 45;
    public int ContinuousSyncStaleHours { get; set; } = 24;
    public int OperationalAlertCooldownHours { get; set; } = 24;
    public int StorageWarningPercent { get; set; } = 80;

    internal bool IsValid() =>
        MaxConcurrentIngestions is >= 1 and <= 64
        && MaxConcurrentReportAdmissions is >= 1 and <= 8
        && MaxConcurrentReportAdmissions <= MaxConcurrentIngestions
        && ProjectDailyReportLimit > 0
        && ProjectDailyBytesLimit >= 1_048_576
        && RawReportRetentionDays is >= 1 and <= 3650
        && FindingRetentionDays is >= 1 and <= 3650
        && SbomRetentionDays is >= 1 and <= 3650
        && LogRetentionDays is >= 1 and <= 3650
        && EphemeralEnvironmentRetentionHours is >= 1 and <= 168
        && OperationalCheckIntervalMinutes is >= 5 and <= 1440
        && ScannerManifestMaxAgeDays is >= 1 and <= 365
        && ContinuousSyncStaleHours is >= 1 and <= 720
        && OperationalAlertCooldownHours is >= 1 and <= 168
        && StorageWarningPercent is >= 1 and <= 100;
}
