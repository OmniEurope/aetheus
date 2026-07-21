// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.DTOs;

namespace Aetheus.Back.Components.Artifacts;

public interface IArtifactService
{
    Task<PipelineArtifactDto?> PublishArtifactAsync(
        int runId, string name, string? stageName, long contentLength, Stream fileContent, CancellationToken ct = default);
    Task<PaginatedResult<PipelineArtifactDto>> GetProjectArtifactsAsync(int projectId, ProjectArtifactsRequest request, CancellationToken ct = default);
    Task<PipelineArtifactDto?> GetArtifactAsync(int id, CancellationToken ct = default);
    Task<Stream?> DownloadArtifactAsync(int id, CancellationToken ct = default);
    Task<PipelineArtifactDto?> PromoteToEnvironmentAsync(int id, string environmentName, CancellationToken ct = default);
    Task<PipelineArtifactDto?> PromoteToReleaseAsync(int artifactId, int releaseId, CancellationToken ct = default);

    /// <summary>Deploy-success closure: a <c>type: deploy</c> step that the agent reports SUCCEEDED is a
    /// real deployment - apply the <see cref="Aetheus.Shared.Enums.ArtifactRetentionPolicy.Deployed"/>
    /// retention window (so the live artifact is not purged out from under the running app) and flip the
    /// exactly selected linked release to <see cref="Aetheus.Shared.Enums.ReleaseStatus.Deployed"/>. Idempotent;
    /// a missing artifact id is a no-op (returns false).</summary>
    Task<bool> MarkDeployedAsync(
        int artifactId, string environmentName, int? releaseId = null, CancellationToken ct = default);

    Task<bool> IsAgentAssignedToRunAsync(int runId, int serverId, CancellationToken ct = default);

    /// <summary>Cross-agent deploy download (IDOR-safe). The agent is authorised against the
    /// <paramref name="deployRunId"/> it is currently executing (NOT the artifact's build run - those
    /// differ in scenario 3), and the artifact's project org must equal the agent server's org. A
    /// project-less artifact is refused (fail-closed). Returns the open stream only when all checks pass.</summary>
    Task<(DeployDownloadStatus Status, Stream? Stream, string? FileName)> OpenArtifactForAgentAsync(
        int artifactId, int deployRunId, int agentServerId, CancellationToken ct = default);
}

/// <summary>Outcome of <see cref="IArtifactService.OpenArtifactForAgentAsync"/> - maps to 404 / 403 / 200.</summary>
public enum DeployDownloadStatus
{
    Ok = 0,
    NotFound = 1,
    Forbidden = 2
}
