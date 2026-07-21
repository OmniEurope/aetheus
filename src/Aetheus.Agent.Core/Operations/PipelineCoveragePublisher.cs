// SPDX-License-Identifier: EUPL-1.2
using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using Aetheus.Agent.Core.Executors;
using Aetheus.Agent.Core.Services;
using Aetheus.Shared.Enums;

namespace Aetheus.Agent.Core.Operations;

internal static class PipelineCoveragePublisher
{
    public static async Task<ExecutorResult> PublishAsync(
        IServerApiClient apiClient, ILogger logger, string target,
        IReadOnlyDictionary<string, string> envVars,
        Func<string, TaskLogLevel, Task> onOutput, CancellationToken ct)
    {
        envVars.TryGetValue("AETHEUS_RUN_ID", out var runIdText);
        envVars.TryGetValue("AETHEUS_STAGE_NAME", out var stageName);
        envVars.TryGetValue("AETHEUS_WORKING_DIR", out var workingDir);
        if (!int.TryParse(runIdText, out var runId))
        {
            await onOutput("Missing AETHEUS_RUN_ID", TaskLogLevel.Error).ConfigureAwait(false);
            return new ExecutorResult(-1, false);
        }

        var baseDir = !string.IsNullOrEmpty(workingDir) ? workingDir : Directory.GetCurrentDirectory();
        var patterns = ParsePatterns(target);
        await onOutput(
            $"Coverage workspace: '{baseDir}' (exists={Directory.Exists(baseDir)}); patterns: {string.Join(", ", patterns)}",
            TaskLogLevel.Info).ConfigureAwait(false);
        var files = patterns
            .SelectMany(pattern => WorkspaceFileMatcher.GetMatchingFiles(baseDir, pattern))
            .Where(File.Exists)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(file => file, StringComparer.Ordinal)
            .ToList();
        if (files.Count == 0)
        {
            foreach (var pattern in patterns.Where(pattern => !pattern.Contains('*') && !pattern.Contains('?')))
            {
                var candidate = Path.Combine(baseDir, pattern);
                await onOutput(
                    $"Coverage candidate missing: '{candidate}' (file={File.Exists(candidate)}, directory={Directory.Exists(Path.GetDirectoryName(candidate))})",
                    TaskLogLevel.Error).ConfigureAwait(false);
            }
            await onOutput("No coverage files found matching patterns - failing (coverage step published nothing).", TaskLogLevel.Error).ConfigureAwait(false);
            return new ExecutorResult(1, false);
        }
        if (files.Count != 1)
        {
            await onOutput($"Coverage publish requires one merged Cobertura report; found {files.Count} raw reports.", TaskLogLevel.Error).ConfigureAwait(false);
            return new ExecutorResult(1, false);
        }

        if (!await MatchesExpectedCommitAsync(baseDir, envVars, ct).ConfigureAwait(false))
        {
            await onOutput("Coverage source SHA does not match the checked-out workspace.", TaskLogLevel.Error).ConfigureAwait(false);
            return new ExecutorResult(1, false);
        }

        var file = files[0];
        var xml = await File.ReadAllTextAsync(file, ct).ConfigureAwait(false);
        if (!TryReadTotals(xml, out var covered, out var valid))
        {
            await onOutput("Coverage report is empty, malformed or has invalid line totals.", TaskLogLevel.Error).ConfigureAwait(false);
            return new ExecutorResult(1, false);
        }

        var linePercent = 100.0 * covered / valid;
        var relativePath = Path.GetRelativePath(baseDir, file);
        await onOutput($"Publishing merged coverage from {relativePath}: {linePercent:0.##}% ({covered}/{valid})", TaskLogLevel.Info).ConfigureAwait(false);
        try
        {
            await apiClient.PublishCoverageAsync(runId, xml, stageName, relativePath, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to publish coverage for run {RunId}", runId);
            await onOutput($"Coverage publish failed: {ex.Message}", TaskLogLevel.Error).ConfigureAwait(false);
            return new ExecutorResult(-1, false);
        }

        if (envVars.TryGetValue("AETHEUS_MIN_COVERAGE", out var minimumText)
            && double.TryParse(minimumText, NumberStyles.Float, CultureInfo.InvariantCulture, out var minimum)
            && linePercent < minimum)
        {
            await onOutput($"Coverage below threshold: {linePercent:0.##}% < {minimum:0.##}%", TaskLogLevel.Error).ConfigureAwait(false);
            return new ExecutorResult(1, false);
        }

        await onOutput("Coverage published successfully", TaskLogLevel.Info).ConfigureAwait(false);
        return new ExecutorResult(0, false);
    }

    private static List<string> ParsePatterns(string target)
    {
        if (string.IsNullOrWhiteSpace(target)) return ["**/coverage.cobertura.xml"];
        try { return JsonSerializer.Deserialize<List<string>>(target) ?? ["**/coverage.cobertura.xml"]; }
        catch (JsonException) { return [target]; }
    }

    private static async Task<bool> MatchesExpectedCommitAsync(
        string baseDir, IReadOnlyDictionary<string, string> envVars, CancellationToken ct)
    {
        var expected = envVars.GetValueOrDefault("BUILD_SOURCEVERSION");
        if (string.IsNullOrWhiteSpace(expected)) expected = envVars.GetValueOrDefault("AETHEUS_SOURCE_COMMIT");
        if (string.IsNullOrWhiteSpace(expected) || !Directory.Exists(Path.Combine(baseDir, ".git"))) return true;

        var startInfo = new System.Diagnostics.ProcessStartInfo("git")
        {
            WorkingDirectory = baseDir,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("rev-parse");
        startInfo.ArgumentList.Add("HEAD");
        using var process = System.Diagnostics.Process.Start(startInfo);
        if (process is null) return false;
        var actual = (await process.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false)).Trim();
        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        return process.ExitCode == 0 && actual.Equals(expected, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryReadTotals(string xml, out long covered, out long valid)
    {
        covered = 0;
        valid = 0;
        try
        {
            var root = XDocument.Parse(xml).Root;
            return root?.Name.LocalName == "coverage"
                && long.TryParse(root.Attribute("lines-covered")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out covered)
                && long.TryParse(root.Attribute("lines-valid")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out valid)
                && valid > 0
                && covered >= 0
                && covered <= valid;
        }
        catch (System.Xml.XmlException)
        {
            return false;
        }
    }
}
