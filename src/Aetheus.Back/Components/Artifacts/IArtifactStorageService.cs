// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Components.Artifacts;

public interface IArtifactStorageService
{
    /// <summary>Streams the artifact to disk and returns its relative path plus the SHA-256 (lowercase hex) of the stored bytes.</summary>
    Task<(string RelativePath, string Sha256)> SaveArtifactAsync(int projectId, int pipelineId, int runId, string fileName, Stream content, CancellationToken ct = default);
    Task DeleteArtifactAsync(string filePath, CancellationToken ct = default);
    Stream? OpenArtifact(string filePath);
    long GetArtifactSize(string filePath);
}
