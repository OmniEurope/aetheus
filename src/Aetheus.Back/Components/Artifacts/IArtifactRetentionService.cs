// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Artifacts;

public interface IArtifactRetentionService
{
    Task ApplyBuildRetentionAsync(PipelineArtifact newArtifact, CancellationToken ct = default);
    Task ApplyDeployRetentionAsync(PipelineArtifact artifact, string environmentName, CancellationToken ct = default);
    Task ApplyReleaseRetentionAsync(PipelineArtifact artifact, int releaseId, CancellationToken ct = default);
}
