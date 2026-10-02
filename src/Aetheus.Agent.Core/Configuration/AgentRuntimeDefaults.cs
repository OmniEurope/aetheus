// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Agent.Core.Configuration;

/// <summary>Operational defaults shared by agent services and their configuration binding.</summary>
public static class AgentRuntimeDefaults
{
    public const int PollingIntervalSeconds = 10;

    /// <summary>Poll interval while this agent is part of an active run: a task is running, or one was
    /// claimed or finished within <see cref="ActivePollingWindow"/>. The next stage of a run is created
    /// the moment the previous one completes, and a 10 s poll made every stage boundary wait for it
    /// (PLAN-007 lot 7).</summary>
    public static readonly TimeSpan ActivePollingInterval = TimeSpan.FromSeconds(2);

    /// <summary>How long after its last task activity the agent keeps the active interval. Covers the
    /// gap between two stages of the same run, including an artifact collection in between.</summary>
    public static readonly TimeSpan ActivePollingWindow = TimeSpan.FromMinutes(2);
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

    /// <summary>Recette R2-015: how often the storage measurement (<c>docker system df</c>, directory and
    /// journal sizes) is refreshed. It runs in the background, outside the heartbeat collection budget,
    /// because it takes minutes during builds; the sizes it reports move slowly, and the build state the
    /// deployment-only alert needs is read live on every beat instead.</summary>
    public static readonly TimeSpan StorageDiagnosticsTtl = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan LivenessCheckInterval = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan BackendRequestTimeout = TimeSpan.FromMinutes(10);
}
