// SPDX-License-Identifier: EUPL-1.2
using System.IO.Compression;
using System.Security.Cryptography;
using Aetheus.Agent.Core.Executors;
using Aetheus.Agent.Core.Services;
using Aetheus.Shared.Enums;

namespace Aetheus.Agent.Core.Operations;

/// <summary>
/// Downloads and safely restores a verified pipeline artifact into the agent workspace.
/// </summary>
internal static class PipelineArtifactRestorer
{
    private const long SafetyFloorBytes = 512L * 1024 * 1024;

    public static async Task<ExecutorResult> RestoreAsync(
        IServerApiClient apiClient, ILogger logger, IReadOnlyDictionary<string, string> envVars,
        Func<string, TaskLogLevel, Task> onOutput, CancellationToken ct,
        Func<string, long>? availableSpaceProvider = null)
    {
        envVars.TryGetValue("AETHEUS_RESTORE_ARTIFACT_ID", out var artifactIdText);
        envVars.TryGetValue("AETHEUS_RESTORE_RUN_ID", out var runIdText);
        envVars.TryGetValue("AETHEUS_RESTORE_ARTIFACT_SHA256", out var expectedSha256);
        envVars.TryGetValue("AETHEUS_WORKING_DIR", out var workingDir);
        envVars.TryGetValue("AETHEUS_RESTORE_TARGET_DIR", out var targetDirectory);
        if (!int.TryParse(artifactIdText, out var artifactId) || artifactId <= 0
            || !int.TryParse(runIdText, out var runId) || runId <= 0
            || string.IsNullOrWhiteSpace(workingDir)
            || expectedSha256 is null
            || expectedSha256.Length != 64
            || expectedSha256.Any(character => !Uri.IsHexDigit(character)))
        {
            await onOutput(
                "Missing or invalid AETHEUS_RESTORE_ARTIFACT_ID, AETHEUS_RESTORE_RUN_ID, " +
                "AETHEUS_RESTORE_ARTIFACT_SHA256 or AETHEUS_WORKING_DIR",
                TaskLogLevel.Error).ConfigureAwait(false);
            return new ExecutorResult(-1, false);
        }

        try
        {
            var restoreDirectory = ResolveRestoreDirectory(workingDir, targetDirectory);
            Directory.CreateDirectory(restoreDirectory);
            await onOutput($"Downloading verified artifact {artifactId} into {restoreDirectory}…", TaskLogLevel.Info).ConfigureAwait(false);
            await using var zip = await apiClient.DownloadArtifactAsync(artifactId, runId, ct).ConfigureAwait(false);
            if (zip is null)
            {
                await onOutput("Artifact download failed (not found / forbidden)", TaskLogLevel.Error).ConfigureAwait(false);
                return new ExecutorResult(1, false);
            }

            var actualSha256 = Convert.ToHexString(
                await SHA256.HashDataAsync(zip, ct).ConfigureAwait(false));
            zip.Position = 0;
            if (!actualSha256.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                await onOutput(
                    $"Artifact SHA-256 mismatch: expected {expectedSha256}, received {actualSha256.ToLowerInvariant()}.",
                    TaskLogLevel.Error).ConfigureAwait(false);
                return new ExecutorResult(1, false);
            }

            using var archive = new ZipArchive(zip, ZipArchiveMode.Read);
            var requiredBytes = DeployLayout.ValidateArchive(archive, restoreDirectory);
            var root = Path.GetPathRoot(Path.GetFullPath(workingDir));
            var availableBytes = availableSpaceProvider?.Invoke(root!) ?? new DriveInfo(root!).AvailableFreeSpace;
            if (!HasEnoughSpace(requiredBytes, availableBytes))
            {
                await onOutput(
                    $"Artifact restore refused: {requiredBytes} bytes to unpack with only {availableBytes} bytes free (512 MiB safety floor).",
                    TaskLogLevel.Error).ConfigureAwait(false);
                return new ExecutorResult(1, false);
            }

            DeployLayout.ExtractSafely(archive, restoreDirectory);
            await onOutput($"Restored {archive.Entries.Count} artifact entries into {restoreDirectory}", TaskLogLevel.Info).ConfigureAwait(false);
            return new ExecutorResult(0, false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
                                   or ArgumentException or NotSupportedException)
        {
            logger.LogError(ex, "Failed to restore artifact {ArtifactId} for run {RunId}", artifactId, runId);
            await onOutput($"Artifact restore failed: {ex.Message}", TaskLogLevel.Error).ConfigureAwait(false);
            return new ExecutorResult(1, false);
        }
    }

    internal static bool HasEnoughSpace(long requiredBytes, long availableBytes) =>
        availableBytes >= SafetyFloorBytes && requiredBytes <= availableBytes - SafetyFloorBytes;

    internal static string ResolveRestoreDirectory(string workspace, string? targetDirectory)
    {
        var root = Path.GetFullPath(workspace);
        if (string.IsNullOrWhiteSpace(targetDirectory)) return root;
        if (Path.IsPathRooted(targetDirectory))
            throw new InvalidDataException("Artifact target directory must be relative to the workspace.");

        var destination = Path.GetFullPath(Path.Combine(root, targetDirectory));
        var rootWithSeparator = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!destination.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Artifact target directory escapes the workspace.");
        return destination;
    }
}
