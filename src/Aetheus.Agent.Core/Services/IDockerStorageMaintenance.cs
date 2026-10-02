// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Agent.Core.Services;


public interface IDockerStorageMaintenance
{
    string BuilderName { get; }
    bool DeploymentOnly { get; }
    /// <summary>A build holds the exclusive build lease right now. Cheap: read live by every heartbeat.</summary>
    bool BuildActive { get; }
    /// <summary>When a build last asked for the lease, or null when none has since the agent started.</summary>
    DateTime? LastBuildAttemptAtUtc { get; }
    bool IsBuildCommand(string command);
    /// <summary>
    /// Returns false when <paramref name="waitBudget"/> elapses before the exclusive build lease frees.
    /// A null budget waits indefinitely.
    /// </summary>
    Task<bool> PrepareBuildAsync(
        IDictionary<string, string> environmentVariables,
        CancellationToken ct,
        bool runDockerMaintenance = true,
        TimeSpan? waitBudget = null);
    Task ScheduleBuildCompletionAsync(bool runDockerMaintenance = true, CancellationToken ct = default);
    Task CompleteBuildAsync(CancellationToken ct, bool runDockerMaintenance = true);
    Task RunMaintenanceAsync(string reason, CancellationToken ct);
    Task<StorageDiagnosticsDto> CollectDiagnosticsAsync(CancellationToken ct);
}
