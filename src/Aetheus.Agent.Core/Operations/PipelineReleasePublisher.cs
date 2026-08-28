// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Agent.Core.Operations;

internal static class PipelineReleasePublisher
{
    public static async Task<ExecutorResult> PublishAsync(
        IServerApiClient apiClient, ILogger logger, string version,
        IReadOnlyDictionary<string, string> envVars,
        Func<string, TaskLogLevel, Task> onOutput, CancellationToken ct)
    {
        envVars.TryGetValue("AETHEUS_PROJECT_ID", out var projectIdText);
        envVars.TryGetValue("AETHEUS_RUN_ID", out var runIdText);
        envVars.TryGetValue("AETHEUS_CHANGELOG", out var changelogFlag);
        envVars.TryGetValue("AETHEUS_WORKING_DIR", out var workingDirectory);
        envVars.TryGetValue("AETHEUS_RELEASE_COMMIT", out var commitHash);
        envVars.TryGetValue("AETHEUS_RELEASE_BRANCH", out var branch);
        envVars.TryGetValue("AETHEUS_RELEASE_ARTIFACT_RUN_ID", out var artifactRunIdText);
        envVars.TryGetValue("AETHEUS_RELEASE_DEPLOYED", out var deployedText);
        if (!int.TryParse(projectIdText, out var projectId) || !int.TryParse(runIdText, out var runId))
        {
            await onOutput("Missing AETHEUS_PROJECT_ID or AETHEUS_RUN_ID", TaskLogLevel.Error).ConfigureAwait(false);
            return new ExecutorResult(-1, false);
        }

        var directory = !string.IsNullOrEmpty(workingDirectory) ? workingDirectory : Directory.GetCurrentDirectory();
        if ((string.IsNullOrWhiteSpace(commitHash) || string.IsNullOrWhiteSpace(branch))
            && Directory.Exists(Path.Combine(directory, ".git")))
        {
            commitHash ??= await RunGitAsync(directory, ct, "rev-parse", "HEAD").ConfigureAwait(false);
            branch ??= await RunGitAsync(directory, ct, "rev-parse", "--abbrev-ref", "HEAD").ConfigureAwait(false);
        }

        var changelog = string.Equals(changelogFlag, "true", StringComparison.OrdinalIgnoreCase)
            ? await GenerateChangelogAsync(directory, onOutput, ct).ConfigureAwait(false)
            : null;
        var artifactRunId = int.TryParse(artifactRunIdText, out var parsedArtifactRunId) && parsedArtifactRunId > 0
            ? parsedArtifactRunId : (int?)null;
        var deployed = string.Equals(deployedText, "true", StringComparison.OrdinalIgnoreCase);
        await onOutput($"Creating release {version}...", TaskLogLevel.Info).ConfigureAwait(false);
        if (commitHash is not null)
            await onOutput($"Commit: {commitHash[..Math.Min(8, commitHash.Length)]}, Branch: {branch}", TaskLogLevel.Info).ConfigureAwait(false);

        try
        {
            var result = await apiClient.CreateReleaseAsync(
                projectId, runId, version, changelog, commitHash, branch, artifactRunId, deployed, ct).ConfigureAwait(false);
            if (result is null)
            {
                await onOutput("Release creation failed: the server returned no release", TaskLogLevel.Error).ConfigureAwait(false);
                return new ExecutorResult(-1, false);
            }

            await onOutput($"Release {result.Version} (build #{result.BuildNumber}) created", TaskLogLevel.Info).ConfigureAwait(false);
            await onOutput($"##aetheus[setvariable name=RELEASE_ID]{result.Id}", TaskLogLevel.Info).ConfigureAwait(false);
            return new ExecutorResult(0, false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create release for run {RunId}", runId);
            await onOutput($"Release creation failed: {ex.Message}", TaskLogLevel.Error).ConfigureAwait(false);
            return new ExecutorResult(-1, false);
        }
    }

    private static async Task<string?> GenerateChangelogAsync(
        string workingDirectory, Func<string, TaskLogLevel, Task> onOutput, CancellationToken ct)
    {
        if (!Directory.Exists(Path.Combine(workingDirectory, ".git")))
        {
            await onOutput("No .git directory found, skipping changelog", TaskLogLevel.Warning).ConfigureAwait(false);
            return null;
        }
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo("git")
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("log");
            startInfo.ArgumentList.Add("--oneline");
            startInfo.ArgumentList.Add("--no-decorate");
            startInfo.ArgumentList.Add("-50");
            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process is null) return null;
            var output = await process.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            if (process.ExitCode != 0) return null;
            await onOutput($"Changelog: {output.Split('\n').Length} commits collected", TaskLogLevel.Info).ConfigureAwait(false);
            return output.Length > 10000 ? output[..10000] : output;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static async Task<string?> RunGitAsync(string workingDirectory, CancellationToken ct, params string[] args)
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo("git")
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var argument in args) startInfo.ArgumentList.Add(argument);
            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process is null) return null;
            var output = await process.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            return process.ExitCode == 0 ? output.Trim() : null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}
