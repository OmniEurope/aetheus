// SPDX-License-Identifier: EUPL-1.2
using System.Text.RegularExpressions;

namespace Aetheus.Agent.Core.Services;

internal static class DockerBuildCommandClassifier
{
    // Wrapper nesting is attacker-controlled. Keep a generous defensive ceiling to bound work without
    // making three harmless shell layers a bypass of this deployment-safety classifier.
    private const int MaximumWrapperDepth = 16;
    // The classifier runs while the agent can be saturated by concurrent build/test work. A 100 ms
    // wall-clock timeout intermittently classified tiny, benign commands as builds under CPU pressure.
    // One second remains bounded for attacker-controlled input while avoiding load-induced false positives.
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
    private const string CommandWrapperPattern =
        @"(?:(?:(?:[^\s;&|]+/)?(?:sudo|env|command|exec|nohup))\s+(?:(?:-[^\s;&|]+|[A-Za-z_][A-Za-z0-9_]*=[^\s;&|]+|[A-Za-z0-9_.:@/-]+)\s+)*)*";
    private static readonly Regex DockerBuildPattern = Create(
        @"(?:^|[;&|\n]\s*)" + CommandWrapperPattern
        + @"(?:[^\s;&|]+/)?docker\s+(?:build(?:\s|$)|buildx\s+build(?:\s|$)|compose\b[^\n;&|]*(?:\sbuild(?:\s|$)|\s--build(?:\s|$)))");
    private static readonly Regex ComposeVariableBuildPattern = Create(
        @"(?:^|[;&|\n]\s*)" + CommandWrapperPattern
        + @"\$(?:COMPOSE\b|\{COMPOSE\})[^\n;&|]*(?:\sbuild(?:\s|$)|\s--build(?:\s|$))");
    private static readonly Regex ShellWrappedCommandPattern = Create(
        @"(?:^|[;&|\n]\s*)" + CommandWrapperPattern
        + @"(?:[^\s;&|]+/)?(?:bash|dash|sh|zsh)\s+(?:-[^\s;&|]+\s+)*(?<quote>[""'])(?<command>(?:\\.|(?!\k<quote>).)*)\k<quote>");
    private static readonly Regex DeploymentBuildPattern = Create(
        @"(?<![A-Za-z0-9_.-])(?:[^\s;&|]+/)?docker(?:\.exe)?\s+"
        + @"(?:(?:(?:--context|--host|--config|--log-level|-H)(?:=[^\s;&|]+|\s+[^\s;&|]+))\s+)*"
        + @"(?:build(?:\s|$)|buildx\s+build(?:\s|$)|compose\b[^\n;&|]*(?:\sbuild(?:\s|$)|\s--build(?:\s|$)))");

    internal static (bool IsBuild, bool TimedOut) Classify(string command, bool deploymentOnly)
    {
        try
        {
            return (IsBuildCommand(command, deploymentOnly, depth: 0), false);
        }
        catch (RegexMatchTimeoutException)
        {
            // Classification is a safety gate. Conservatively classifying an indeterminate command
            // as a build preserves deployment-only enforcement and cannot terminate the task runner.
            return (true, true);
        }
    }

    private static bool IsBuildCommand(string command, bool deploymentOnly, int depth)
    {
        if (string.IsNullOrWhiteSpace(command)) return false;
        foreach (var line in command.Split('\n'))
        {
            if (ComposeVariableBuildPattern.IsMatch(line)
                || DockerBuildPattern.IsMatch(line)
                || (deploymentOnly && DeploymentBuildPattern.IsMatch(line)))
            {
                return true;
            }
        }
        // Reaching the recursion ceiling is indeterminate, not proof that the nested command is safe.
        // On deployment-only agents this classifier is an authorization guard, so fail closed.
        if (depth >= MaximumWrapperDepth) return deploymentOnly;

        return ShellWrappedCommandPattern.Matches(command)
            .Select(match =>
            {
                var nested = match.Groups["command"].Value;
                var quote = match.Groups["quote"].Value;
                return quote.Length == 0
                    ? nested
                    : nested.Replace($"\\{quote}", quote, StringComparison.Ordinal);
            })
            .Any(nested => IsBuildCommand(nested, deploymentOnly, depth + 1));
    }

    private static Regex Create(string pattern) => new(
        pattern,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        RegexTimeout);
}
