// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Agent.Core.Services;

using Aetheus.Shared.DTOs;

public interface IDockerStorageMaintenance
{
    string BuilderName { get; }
    bool DeploymentOnly { get; }
    bool IsBuildCommand(string command);
    Task PrepareBuildAsync(
        IDictionary<string, string> environmentVariables,
        CancellationToken ct,
        bool runDockerMaintenance = true);
    Task CompleteBuildAsync(CancellationToken ct, bool runDockerMaintenance = true);
    Task RunMaintenanceAsync(string reason, CancellationToken ct);
    Task<StorageDiagnosticsDto> CollectDiagnosticsAsync(CancellationToken ct);
}
