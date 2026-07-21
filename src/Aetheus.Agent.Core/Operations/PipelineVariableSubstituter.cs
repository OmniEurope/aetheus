// SPDX-License-Identifier: EUPL-1.2
using System.Text.Json;
using Aetheus.Agent.Core.Executors;
using Aetheus.Shared.Enums;

namespace Aetheus.Agent.Core.Operations;

internal static class PipelineVariableSubstituter
{
    public static async Task<ExecutorResult> SubstituteAsync(
        string target, IReadOnlyDictionary<string, string> envVars,
        Func<string, TaskLogLevel, Task> onOutput, CancellationToken ct)
    {
        envVars.TryGetValue("AETHEUS_WORKING_DIR", out var workingDirectory);
        var baseDirectory = !string.IsNullOrEmpty(workingDirectory) ? workingDirectory : Directory.GetCurrentDirectory();
        List<string>? patterns = null;
        if (!string.IsNullOrWhiteSpace(target))
        {
            try { patterns = JsonSerializer.Deserialize<List<string>>(target); }
            catch (JsonException) { patterns = [target]; }
        }
        if (patterns is null or { Count: 0 })
        {
            await onOutput("Variable substitution failed: no target files were specified", TaskLogLevel.Error).ConfigureAwait(false);
            return new ExecutorResult(-1, false);
        }

        var replacements = 0;
        var filesMatched = 0;
        var filesProcessed = 0;
        foreach (var pattern in patterns)
        {
            foreach (var file in WorkspaceFileMatcher.GetMatchingFiles(baseDirectory, pattern))
            {
                if (!File.Exists(file)) continue;
                filesMatched++;
                var content = await File.ReadAllTextAsync(file, ct).ConfigureAwait(false);
                var original = content;
                foreach (var (key, value) in envVars)
                {
                    var token = $"#{{{key}}}#";
                    if (!content.Contains(token, StringComparison.Ordinal)) continue;
                    content = content.Replace(token, value, StringComparison.Ordinal);
                    replacements++;
                }
                if (content == original) continue;

                await File.WriteAllTextAsync(file, content, ct).ConfigureAwait(false);
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                await onOutput($"  Substituted: {Path.GetRelativePath(baseDirectory, file)}", TaskLogLevel.Info).ConfigureAwait(false);
                filesProcessed++;
            }
        }

        if (filesMatched == 0)
        {
            await onOutput("Variable substitution failed: no target files matched", TaskLogLevel.Error).ConfigureAwait(false);
            return new ExecutorResult(-1, false);
        }

        await onOutput($"Substitution complete: {replacements} token(s) in {filesProcessed} file(s)", TaskLogLevel.Info).ConfigureAwait(false);
        return new ExecutorResult(0, false);
    }
}
