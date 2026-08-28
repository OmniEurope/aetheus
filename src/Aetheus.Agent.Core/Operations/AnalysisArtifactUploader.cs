// SPDX-License-Identifier: EUPL-1.2
using System.IO.Compression;
using System.Text;

namespace Aetheus.Agent.Core.Operations;

internal static class AnalysisArtifactUploader
{
    public static async Task<int> UploadFileAsync(
        IServerApiClient api,
        int runId,
        string artifactName,
        string? stageName,
        string entryName,
        string filePath,
        CancellationToken ct)
    {
        await using var input = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        return await UploadAsync(api, runId, artifactName, stageName, entryName, input, ct).ConfigureAwait(false);
    }

    public static async Task<int> UploadTextAsync(
        IServerApiClient api,
        int runId,
        string artifactName,
        string? stageName,
        string entryName,
        string content,
        CancellationToken ct)
    {
        await using var input = new MemoryStream(Encoding.UTF8.GetBytes(content), writable: false);
        return await UploadAsync(api, runId, artifactName, stageName, entryName, input, ct).ConfigureAwait(false);
    }

    private static async Task<int> UploadAsync(
        IServerApiClient api,
        int runId,
        string artifactName,
        string? stageName,
        string entryName,
        Stream input,
        CancellationToken ct)
    {
        await using var zip = new MemoryStream();
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry(entryName.Replace('\\', '/'), CompressionLevel.SmallestSize);
            await using var output = entry.Open();
            await input.CopyToAsync(output, ct).ConfigureAwait(false);
        }
        zip.Position = 0;
        var artifact = await api.UploadArtifactAsync(runId, artifactName, stageName, zip, ct).ConfigureAwait(false);
        return artifact?.Id
            ?? throw new InvalidOperationException($"Analysis artifact '{artifactName}' upload returned no artifact.");
    }
}
