// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Agent.Core.Configuration;

/// <summary>
/// Bounded, application-owned Docker storage policy. Mutation is enabled by default, but remains
/// restricted to the agent-owned Buildx builder and expired non-latest NuGet package versions.
/// </summary>
public sealed class DockerStorageMaintenanceOptions
{
    public const int CurrentPolicyVersion = 3;

    public int PolicyVersion { get; set; } = CurrentPolicyVersion;
    public bool Enabled { get; set; } = true;
    public bool DryRun { get; set; }
    public bool DeploymentOnly { get; set; }
    public bool AllowBuildsOnDeploymentTarget { get; set; }
    public string? BuilderName { get; set; }
    public int MaintenanceIntervalMinutes { get; set; } = 60;
    public int MaxCacheAgeHours { get; set; } = 168;
    public int PressureCacheAgeHours { get; set; } = 24;
    public int ReservedSpaceGiB { get; set; } = 5;
    public int MaxCacheGiB { get; set; } = 15;
    public int MinFreeSpaceGiB { get; set; } = 20;
    public int PressureUsedPercent { get; set; } = 80;
    public string? NuGetPackagesPath { get; set; }
    public int NuGetCacheRetentionDays { get; set; } = 30;
}
