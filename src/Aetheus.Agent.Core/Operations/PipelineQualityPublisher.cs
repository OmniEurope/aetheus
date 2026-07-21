// SPDX-License-Identifier: EUPL-1.2
using System.Text.Json;
using Aetheus.Agent.Core.Executors;
using Aetheus.Agent.Core.Services;
using Aetheus.Shared.Enums;

namespace Aetheus.Agent.Core.Operations;

internal static class PipelineQualityPublisher
{
    private static readonly string[] s_skipDirectories = ["bin", "obj", ".git", "node_modules", ".vs"];

    public static async Task<ExecutorResult> PublishComplexityAsync(
        IServerApiClient apiClient, ILogger logger, IReadOnlyDictionary<string, string> envVars,
        Func<string, TaskLogLevel, Task> onOutput, CancellationToken ct)
    {
        envVars.TryGetValue("AETHEUS_RUN_ID", out var runIdText);
        envVars.TryGetValue("AETHEUS_STAGE_NAME", out var stageName);
        envVars.TryGetValue("AETHEUS_WORKING_DIR", out var workingDirectory);
        if (!int.TryParse(runIdText, out var runId))
        {
            await onOutput("Missing AETHEUS_RUN_ID", TaskLogLevel.Error).ConfigureAwait(false);
            return new ExecutorResult(-1, false);
        }

        var baseDirectory = !string.IsNullOrEmpty(workingDirectory) ? workingDirectory : Directory.GetCurrentDirectory();
        await onOutput($"Analyzing C# complexity in: {baseDirectory}", TaskLogLevel.Info).ConfigureAwait(false);
        var sources = new List<(string Path, string Content)>();
        foreach (var file in EnumerateCsFiles(baseDirectory))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                sources.Add((file, await File.ReadAllTextAsync(file, ct).ConfigureAwait(false)));
            }
            catch (IOException)
            {
                // An unreadable source cannot contribute to the report.
            }
        }

        if (sources.Count == 0)
        {
            await onOutput("No C# source files found - failing (complexity step analyzed nothing).", TaskLogLevel.Error).ConfigureAwait(false);
            return new ExecutorResult(1, false);
        }

        var report = ComplexityAnalyzer.Analyze(sources);
        await onOutput(
            $"Analyzed {sources.Count} files · {report.TotalMethods} methods · avg CC {report.AvgCyclomatic} · max CC {report.MaxCyclomatic} · {report.HighComplexityMethods} over {ComplexityAnalyzer.HighComplexityThreshold} · {report.TotalLinesOfCode} LOC",
            TaskLogLevel.Info).ConfigureAwait(false);
        try
        {
            await apiClient.PublishComplexityAsync(runId, report.AvgCyclomatic, report.MaxCyclomatic,
                report.TotalMethods, report.HighComplexityMethods, report.TotalLinesOfCode, stageName, ct).ConfigureAwait(false);
            await onOutput("Complexity metrics published successfully", TaskLogLevel.Info).ConfigureAwait(false);
            if (envVars.TryGetValue("AETHEUS_MAX_COMPLEXITY", out var maximumText)
                && int.TryParse(maximumText, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out var maximum)
                && report.MaxCyclomatic > maximum)
            {
                await onOutput($"Complexity budget exceeded: max CC {report.MaxCyclomatic} > {maximum}", TaskLogLevel.Error).ConfigureAwait(false);
                return new ExecutorResult(1, false);
            }
            return new ExecutorResult(0, false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to publish complexity for run {RunId}", runId);
            await onOutput($"Complexity publish failed: {ex.Message}", TaskLogLevel.Error).ConfigureAwait(false);
            return new ExecutorResult(-1, false);
        }
    }

    public static async Task<ExecutorResult> PublishLintAsync(
        IServerApiClient apiClient, ILogger logger, string target,
        IReadOnlyDictionary<string, string> envVars,
        Func<string, TaskLogLevel, Task> onOutput, CancellationToken ct)
    {
        envVars.TryGetValue("AETHEUS_RUN_ID", out var runIdText);
        envVars.TryGetValue("AETHEUS_STAGE_NAME", out var stageName);
        envVars.TryGetValue("AETHEUS_WORKING_DIR", out var workingDirectory);
        if (!int.TryParse(runIdText, out var runId))
        {
            await onOutput("Missing AETHEUS_RUN_ID", TaskLogLevel.Error).ConfigureAwait(false);
            return new ExecutorResult(-1, false);
        }

        var baseDirectory = !string.IsNullOrEmpty(workingDirectory) ? workingDirectory : Directory.GetCurrentDirectory();
        List<string>? patterns = null;
        if (!string.IsNullOrWhiteSpace(target))
        {
            try { patterns = JsonSerializer.Deserialize<List<string>>(target); }
            catch (JsonException) { patterns = [target]; }
        }
        patterns ??= ["**/*.sarif"];

        foreach (var pattern in patterns)
        {
            foreach (var file in WorkspaceFileMatcher.GetMatchingFiles(baseDirectory, pattern))
            {
                if (!File.Exists(file)) continue;
                var sarif = await File.ReadAllTextAsync(file, ct).ConfigureAwait(false);
                var relativePath = Path.GetRelativePath(baseDirectory, file);
                await onOutput($"Publishing lint report from {relativePath}...", TaskLogLevel.Info).ConfigureAwait(false);
                try
                {
                    await apiClient.PublishLintAsync(runId, sarif, stageName, relativePath, ct).ConfigureAwait(false);
                    await onOutput("Lint report published successfully", TaskLogLevel.Info).ConfigureAwait(false);
                    return new ExecutorResult(0, false);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to publish lint report for run {RunId}", runId);
                    await onOutput($"Lint publish failed: {ex.Message}", TaskLogLevel.Error).ConfigureAwait(false);
                    return new ExecutorResult(-1, false);
                }
            }
        }

        await onOutput("No SARIF files found matching patterns - failing (lint step published nothing).", TaskLogLevel.Error).ConfigureAwait(false);
        return new ExecutorResult(1, false);
    }

    private static IEnumerable<string> EnumerateCsFiles(string baseDirectory)
    {
        if (!Directory.Exists(baseDirectory)) return [];
        return Directory.EnumerateFiles(baseDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
                && !file.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase)
                && !PathHasSkippedSegment(file, baseDirectory));
    }

    private static bool PathHasSkippedSegment(string file, string baseDirectory)
    {
        var relativePath = Path.GetRelativePath(baseDirectory, file);
        var segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(segment => s_skipDirectories.Contains(segment, StringComparer.OrdinalIgnoreCase));
    }
}
