// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Agent.Core.Configuration;

/// <summary>Operational defaults shared by agent services and their configuration binding.</summary>
public static class AgentRuntimeDefaults
{
    public const int PollingIntervalSeconds = 10;
    public const int HeartbeatIntervalSeconds = 30;
    public const int HeartbeatCollectionTimeoutSeconds = 15;
    public const int MaximumConcurrentTasks = 2;
    public const int LogRetentionDays = 30;
    public const int MinimumTaskTimeoutSeconds = 10;
    public const int MaximumTaskTimeoutSeconds = 3600;
    public const int DeployReleasesToKeep = 3;
    public const int DeployMinimumFreeSpaceMiB = 512;
    public const int DeployUnpackRatioEstimate = 4;

    public static readonly TimeSpan CapabilityProbeTimeout = TimeSpan.FromSeconds(8);
    public static readonly TimeSpan ShellCommandTimeout = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan StartupRetryDelay = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan DockerInventoryTtl = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan SlowInventoryTtl = TimeSpan.FromSeconds(180);
    public static readonly TimeSpan LivenessCheckInterval = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan BackendRequestTimeout = TimeSpan.FromMinutes(10);
}
