// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Components.AppMonitoring;

/// <summary>
/// PLAN-001 phase 2 "zero-config": computes the OTEL environment a deployed app needs to push telemetry
/// to Aetheus, when a <see cref="Data.Entities.MonitoredApp"/> is linked to the deploy's project/environment.
/// Returns an empty map (no-op) when the feature is unconfigured or no app is linked, so it never affects
/// a deploy that doesn't opt in.
/// </summary>
public interface IAppDeployEnvProvider
{
    Task<IReadOnlyDictionary<string, string>> GetDeployEnvAsync(int projectId, int? environmentId, int pipelineRunId, CancellationToken ct = default);
}
