// SPDX-License-Identifier: EUPL-1.2
using System.Diagnostics;

namespace Aetheus.Agent.Core.Operations;

internal sealed record GitleaksHistorySelection(
    string Mode,
    string? BaselineSha,
    string HeadSha,
    string? LogOptions,
    string Reason);

internal static class GitleaksHistoryModeResolver
{
    private const int MaximumDeepen = 5000;
    private static readonly string[] FullScanTriggers =
    [
        "scanner-manifest.json",
        ".aetheus/security-rules/",
        "src/Aetheus.Agent.Core/AnalysisRules/",
        ".pipeline/aetheus-security.yaml",
        // The Git-history scan itself lives here since PLAN-007 lot 2.
        ".pipeline/aetheus-security-history.yaml"
    ];

    internal static async Task<GitleaksHistorySelection> ResolveAsync(
        string sourceDirectory,
        IReadOnlyDictionary<string, string> envVars,
        CancellationToken ct)
    {
        var requested = Value(envVars, "AETHEUS_GITLEAKS_MODE")?.ToLowerInvariant() ?? "full";
        if (requested is not ("release-range" or "full"))
            throw new InvalidDataException("AETHEUS_GITLEAKS_MODE must be release-range or full.");

        var head = Value(envVars, "BUILD_SOURCEVERSION") ?? string.Empty;
        if (!IsCommit(head))
            throw new InvalidDataException("Gitleaks history requires a full immutable BUILD_SOURCEVERSION.");
        if (requested == "full")
            return new GitleaksHistorySelection("full", null, head.ToLowerInvariant(), null, "explicit-full");

        var baseline = Value(envVars, "DELIVERY_BASELINE_SOURCE_SHA") ?? string.Empty;
        if (!IsCommit(baseline))
            return new GitleaksHistorySelection("full", null, head.ToLowerInvariant(), null, "missing-baseline");

        var actualHead = await GitOutputAsync(sourceDirectory, ct, "rev-parse", "HEAD").ConfigureAwait(false);
        if (!string.Equals(actualHead, head, StringComparison.OrdinalIgnoreCase))
            return new GitleaksHistorySelection("full", baseline.ToLowerInvariant(), head.ToLowerInvariant(), null, "head-mismatch");

        if (!await GitSucceedsAsync(sourceDirectory, ct, "cat-file", "-e", $"{baseline}^{{commit}}").ConfigureAwait(false))
        {
            var deepen = ParseDepth(Value(envVars, "AETHEUS_GIT_HISTORY_DEPTH"));
            _ = await GitSucceedsAsync(sourceDirectory, ct, "fetch", "--deepen", deepen.ToString(), "origin")
                .ConfigureAwait(false);
        }
        if (!await GitSucceedsAsync(sourceDirectory, ct, "cat-file", "-e", $"{baseline}^{{commit}}").ConfigureAwait(false))
            return new GitleaksHistorySelection("full", baseline.ToLowerInvariant(), head.ToLowerInvariant(), null, "baseline-unavailable");
        if (!await GitSucceedsAsync(sourceDirectory, ct, "merge-base", "--is-ancestor", baseline, head).ConfigureAwait(false))
            return new GitleaksHistorySelection("full", baseline.ToLowerInvariant(), head.ToLowerInvariant(), null, "baseline-not-ancestor");

        var changed = await GitOutputAsync(sourceDirectory, ct, "diff", "--name-only", $"{baseline}..{head}")
            .ConfigureAwait(false);
        if (changed is null || changed.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Any(path => FullScanTriggers.Any(trigger =>
                    path.Trim().StartsWith(trigger, StringComparison.OrdinalIgnoreCase))))
            return new GitleaksHistorySelection("full", baseline.ToLowerInvariant(), head.ToLowerInvariant(), null,
                changed is null ? "change-detection-failed" : "scanner-contract-changed");

        return new GitleaksHistorySelection(
            "release-range",
            baseline.ToLowerInvariant(),
            head.ToLowerInvariant(),
            $"{baseline.ToLowerInvariant()}..{head.ToLowerInvariant()}",
            "validated-release-range");
    }

    internal static async Task<IReadOnlyDictionary<string, string>> ResolveEnvironmentAsync(
        ScannerManifestEntry scanner,
        string sourceDirectory,
        IReadOnlyDictionary<string, string> envVars,
        Func<string, TaskLogLevel, Task> onOutput,
        CancellationToken ct)
    {
        if (!string.Equals(scanner.Key, "gitleaks-history", StringComparison.OrdinalIgnoreCase))
            return envVars;

        var selection = await ResolveAsync(sourceDirectory, envVars, ct).ConfigureAwait(false);
        var resolved = new Dictionary<string, string>(envVars, StringComparer.OrdinalIgnoreCase);
        resolved.Remove("AETHEUS_GITLEAKS_LOG_OPTIONS");
        if (selection.LogOptions is not null)
            resolved["AETHEUS_GITLEAKS_LOG_OPTIONS"] = selection.LogOptions;
        resolved["AETHEUS_GITLEAKS_HISTORY_MODE_RESOLVED"] = selection.Mode;
        resolved["AETHEUS_GITLEAKS_BASE_SHA_RESOLVED"] = selection.BaselineSha ?? string.Empty;
        resolved["AETHEUS_GITLEAKS_HEAD_SHA_RESOLVED"] = selection.HeadSha;
        resolved["AETHEUS_GITLEAKS_HISTORY_REASON_RESOLVED"] = selection.Reason;
        await onOutput(
            $"Gitleaks history mode={selection.Mode}, baseSha={selection.BaselineSha ?? "none"}, " +
            $"headSha={selection.HeadSha}, reason={selection.Reason}.",
            TaskLogLevel.Info).ConfigureAwait(false);
        await onOutput($"##aetheus[setvariable name=GITLEAKS_HISTORY_MODE]{selection.Mode}", TaskLogLevel.Info)
            .ConfigureAwait(false);
        await onOutput($"##aetheus[setvariable name=GITLEAKS_BASE_SHA]{selection.BaselineSha ?? "none"}", TaskLogLevel.Info)
            .ConfigureAwait(false);
        await onOutput($"##aetheus[setvariable name=GITLEAKS_HEAD_SHA]{selection.HeadSha}", TaskLogLevel.Info)
            .ConfigureAwait(false);
        return resolved;
    }

    internal static bool IsCommit(string value) =>
        value.Length is 40 or 64 && value.All(Uri.IsHexDigit);

    private static int ParseDepth(string? value) =>
        int.TryParse(value, out var parsed) ? Math.Clamp(parsed, 1, MaximumDeepen) : 1000;

    private static string? Value(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

    private static async Task<bool> GitSucceedsAsync(
        string directory,
        CancellationToken ct,
        params string[] arguments) =>
        await RunGitAsync(directory, ct, arguments).ConfigureAwait(false) is { ExitCode: 0 };

    private static async Task<string?> GitOutputAsync(
        string directory,
        CancellationToken ct,
        params string[] arguments)
    {
        var result = await RunGitAsync(directory, ct, arguments).ConfigureAwait(false);
        return result.ExitCode == 0 ? result.Output.Trim() : null;
    }

    private static async Task<(int ExitCode, string Output)> RunGitAsync(
        string directory,
        CancellationToken ct,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        GitRepositoryEnvironment.Neutralize(startInfo.Environment);
        using var process = Process.Start(startInfo)
            ?? throw new IOException("Git could not be started for Gitleaks history validation.");
        var output = await process.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        return (process.ExitCode, output);
    }
}
