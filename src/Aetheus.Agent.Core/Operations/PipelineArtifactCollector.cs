// SPDX-License-Identifier: EUPL-1.2
using System.IO.Compression;
using System.Text.Json;
using Aetheus.Agent.Core.Executors;
using Aetheus.Agent.Core.Services;
using Aetheus.Shared.Enums;

namespace Aetheus.Agent.Core.Operations;

internal static class PipelineArtifactCollector
{
    public static async Task<ExecutorResult> CollectAsync(
        IServerApiClient apiClient, ILogger logger, string target,
        IReadOnlyDictionary<string, string> envVars,
        Func<string, TaskLogLevel, Task> onOutput, CancellationToken ct)
    {
        envVars.TryGetValue("AETHEUS_ARTIFACT_NAME", out var artifactName);
        envVars.TryGetValue("AETHEUS_RUN_ID", out var runIdText);
        envVars.TryGetValue("AETHEUS_STAGE_NAME", out var stageName);
        envVars.TryGetValue("AETHEUS_WORKING_DIR", out var workingDirectory);
        if (!int.TryParse(runIdText, out var runId))
        {
            await onOutput("Missing AETHEUS_RUN_ID", TaskLogLevel.Error).ConfigureAwait(false);
            return new ExecutorResult(-1, false);
        }

        artifactName ??= "build-artifact";
        var baseDirectory = !string.IsNullOrEmpty(workingDirectory) ? workingDirectory : Directory.GetCurrentDirectory();
        List<string>? patterns = null;
        if (!string.IsNullOrWhiteSpace(target))
        {
            try { patterns = JsonSerializer.Deserialize<List<string>>(target); }
            catch (JsonException) { patterns = [target]; }
        }

        await onOutput($"Collecting artifacts from {baseDirectory}...", TaskLogLevel.Info).ConfigureAwait(false);
        var temporaryZip = Path.Combine(Path.GetTempPath(), $"aetheus-artifact-{Guid.NewGuid():N}.zip");
        try
        {
            if (patterns is null or { Count: 0 })
            {
                ZipFile.CreateFromDirectory(baseDirectory, temporaryZip, CompressionLevel.Optimal, includeBaseDirectory: false);
            }
            else
            {
                var matchingFiles = patterns
                    .SelectMany(pattern => WorkspaceFileMatcher.GetMatchingFiles(baseDirectory, pattern))
                    .Where(File.Exists)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                if (matchingFiles.Count == 0)
                {
                    await onOutput("Artifact collection failed: no files matched the configured patterns", TaskLogLevel.Error).ConfigureAwait(false);
                    return new ExecutorResult(-1, false);
                }

                using var archive = ZipFile.Open(temporaryZip, ZipArchiveMode.Create);
                foreach (var file in matchingFiles)
                {
                    var entryName = Path.GetRelativePath(baseDirectory, file);
                    archive.CreateEntryFromFile(file, entryName, CompressionLevel.Optimal);
                }
            }

            var fileInfo = new FileInfo(temporaryZip);
            await onOutput($"Artifact zip created: {fileInfo.Length / 1024}KB", TaskLogLevel.Info).ConfigureAwait(false);
            await using var stream = new FileStream(temporaryZip, FileMode.Open, FileAccess.Read);
            await apiClient.UploadArtifactAsync(runId, artifactName, stageName, stream, ct).ConfigureAwait(false);
            await onOutput("Artifact uploaded successfully", TaskLogLevel.Info).ConfigureAwait(false);
            await TryAutoPublishCoverageAsync(apiClient, logger, runId, stageName, baseDirectory, onOutput, ct).ConfigureAwait(false);
            return new ExecutorResult(0, false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to collect/upload artifacts for run {RunId}", runId);
            await onOutput($"Artifact collection failed: {ex.Message}", TaskLogLevel.Error).ConfigureAwait(false);
            return new ExecutorResult(-1, false);
        }
        finally
        {
            if (File.Exists(temporaryZip)) File.Delete(temporaryZip);
        }
    }

    private static async Task TryAutoPublishCoverageAsync(
        IServerApiClient apiClient, ILogger logger, int runId, string? stageName, string baseDirectory,
        Func<string, TaskLogLevel, Task> onOutput, CancellationToken ct)
    {
        try
        {
            var mergedFiles = WorkspaceFileMatcher.GetMatchingFiles(baseDirectory, "**/Cobertura.xml")
                .Distinct(StringComparer.Ordinal).ToList();
            if (mergedFiles.Count > 1)
            {
                await onOutput("Coverage auto-publish skipped: multiple merged Cobertura reports found.", TaskLogLevel.Warning).ConfigureAwait(false);
                return;
            }

            var coverageFile = mergedFiles.SingleOrDefault();
            if (coverageFile is null)
            {
                var rawFiles = WorkspaceFileMatcher.GetMatchingFiles(baseDirectory, "**/*.cobertura.xml")
                    .Concat(WorkspaceFileMatcher.GetMatchingFiles(baseDirectory, "**/coverage.xml"))
                    .Distinct(StringComparer.Ordinal).ToList();
                if (rawFiles.Count > 1)
                {
                    await onOutput("Coverage auto-publish skipped: multiple raw reports require an explicit merged report.", TaskLogLevel.Warning).ConfigureAwait(false);
                    return;
                }
                coverageFile = rawFiles.SingleOrDefault();
            }
            if (coverageFile is null || !File.Exists(coverageFile)) return;

            var xml = await File.ReadAllTextAsync(coverageFile, ct).ConfigureAwait(false);
            var relativePath = Path.GetRelativePath(baseDirectory, coverageFile);
            await apiClient.PublishCoverageAsync(runId, xml, stageName, relativePath, ct).ConfigureAwait(false);
            await onOutput($"Coverage auto-detected and published from {relativePath}", TaskLogLevel.Info).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Auto coverage publish skipped for run {RunId}", runId);
        }
    }
}
