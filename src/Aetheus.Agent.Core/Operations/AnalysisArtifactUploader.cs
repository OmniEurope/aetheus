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
        CancellationToken ct,
        string? provenanceWorkspace = null)
    {
        await using var input = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        return await UploadAsync(api, runId, artifactName, stageName, entryName, input, provenanceWorkspace, ct).ConfigureAwait(false);
    }

    public static async Task<int> UploadTextAsync(
        IServerApiClient api,
        int runId,
        string artifactName,
        string? stageName,
        string entryName,
        string content,
        CancellationToken ct,
        string? provenanceWorkspace = null)
    {
        await using var input = new MemoryStream(Encoding.UTF8.GetBytes(content), writable: false);
        return await UploadAsync(api, runId, artifactName, stageName, entryName, input, provenanceWorkspace, ct).ConfigureAwait(false);
    }

    /// <summary>The three files restore-artifacts demands of anything it restores without a release
    /// selector. An artifact assembled here is built in memory rather than collected from a workspace
    /// path, so it carried none of them and no consumer could restore it.</summary>
    private static readonly string[] ProvenanceFiles =
        ["source-commit", "delivery-contract.json", "artifact-provenance.json"];

    private static async Task<int> UploadAsync(
        IServerApiClient api,
        int runId,
        string artifactName,
        string? stageName,
        string entryName,
        Stream input,
        string? provenanceWorkspace,
        CancellationToken ct)
    {
        await using var zip = new MemoryStream();
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry(entryName.Replace('\\', '/'), CompressionLevel.SmallestSize);
            await using (var output = entry.Open())
                await input.CopyToAsync(output, ct).ConfigureAwait(false);
            await AddProvenanceAsync(archive, provenanceWorkspace, ct).ConfigureAwait(false);
        }
        zip.Position = 0;
        var artifact = await api.UploadArtifactAsync(runId, artifactName, stageName, zip, ct).ConfigureAwait(false);
        return artifact?.Id
            ?? throw new InvalidOperationException($"Analysis artifact '{artifactName}' upload returned no artifact.");
    }

    /// <summary>Copies the provenance the workspace already holds into the artifact, so a summary
    /// published here can be restored like any other build output.
    ///
    /// aetheus-candidate restores these summaries to seal the assurance contract, and run 2281 was
    /// refused on all three at once: "the restored artifact carries no .pipeline-artifacts/
    /// source-commit, so the revision it was built from cannot be proved". The files are present in
    /// every pipeline that publishes a grade, because each restores its CI artifact into the
    /// workspace root before grading anything.
    ///
    /// Absent files are not substituted: a workspace that never restored a CI artifact has no
    /// provenance to give, and inventing one is exactly what the check exists to catch.</summary>
    private static async Task AddProvenanceAsync(ZipArchive archive, string? workspace, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(workspace)) return;
        foreach (var name in ProvenanceFiles)
        {
            var path = Path.Combine(workspace, ".pipeline-artifacts", name);
            if (!File.Exists(path)) continue;
            var entry = archive.CreateEntry($".pipeline-artifacts/{name}", CompressionLevel.SmallestSize);
            await using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
            await using var output = entry.Open();
            await source.CopyToAsync(output, ct).ConfigureAwait(false);
        }
    }
}
