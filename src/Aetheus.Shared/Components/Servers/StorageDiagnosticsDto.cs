// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;

namespace Aetheus.Shared.Components.Servers;

public abstract record StorageUsageDto
{
    public bool BuildCacheAvailable { get; init; }
    public bool DockerInventoryAvailable { get; init; }
    public long BuildCacheBytes { get; init; }
    public long BuildCacheReclaimableBytes { get; init; }
    public long DockerImagesBytes { get; init; }
    public long DockerContainersBytes { get; init; }
    public long DockerVolumesBytes { get; init; }
    public long AgentWorkDirectoryBytes { get; init; }
    public long AgentInstallDirectoryBytes { get; init; }
    public long NuGetCacheBytes { get; init; }
    public long JournalBytes { get; init; }
}

public sealed record StorageDiagnosticsDto : StorageUsageDto
{
    [StringLength(200)]
    public string BuilderName { get; init; } = string.Empty;
    public bool DryRun { get; init; }
    public bool DeploymentOnly { get; init; }
    public bool BuildActive { get; init; }
    public DateTime? LastBuildAttemptAtUtc { get; init; }
    [MaxLength(256)]
    [MaxItemStringLength(4096)]
    public List<string> PartialPathMeasurements { get; init; } = [];
    public DateTime CollectedAtUtc { get; init; }
}
