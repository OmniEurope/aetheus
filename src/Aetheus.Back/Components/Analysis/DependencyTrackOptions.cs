// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Components.Analysis;

public sealed class DependencyTrackOptions
{
    public const string SectionName = "Analysis:DependencyTrack";
    public bool Enabled { get; set; }
    public bool Required { get; set; }
    public string BaseUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public int SyncIntervalMinutes { get; set; } = 30;
    public int ProcessingTimeoutSeconds { get; set; } = 120;
    public int MaxResponseBytes { get; set; } = 52_428_800;
    public int MaxFindings { get; set; } = 100_000;
    public int OutboxPollSeconds { get; set; } = 15;
    public int MaxSubmissionAttempts { get; set; } = 8;
    public int RetryBaseSeconds { get; set; } = 30;

    internal bool IsValid() =>
        (!Required || Enabled)
        && SyncIntervalMinutes is >= 5 and <= 1440
        && ProcessingTimeoutSeconds is >= 10 and <= 600
        && MaxResponseBytes is >= 1_048_576 and <= 104_857_600
        && MaxFindings is >= 1 and <= 100_000
        && OutboxPollSeconds is >= 5 and <= 300
        && MaxSubmissionAttempts is >= 1 and <= 20
        && RetryBaseSeconds is >= 1 and <= 3600;
}
