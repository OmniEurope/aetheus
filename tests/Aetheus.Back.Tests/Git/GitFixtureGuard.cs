// SPDX-License-Identifier: EUPL-1.2
using System.Diagnostics;
using Aetheus.Back.Components.Git;

namespace Aetheus.Back.Tests.Git;

/// <summary>
/// Fails a git fixture before its first write when git resolves a repository, or an origin, outside
/// the fixture's own temporary directory. On 2026-09-17 fixtures silently bound to the real repository
/// and their "origin" became the production remote; this check turns that into an immediate failure.
/// </summary>
internal static class GitFixtureGuard
{
    public static void AssertOwnedBy(string repository, string fixtureRoot)
    {
        var root = Normalize(fixtureRoot);
        var commonDir = Normalize(Git(repository, "rev-parse", "--path-format=absolute", "--git-common-dir").Output);
        Assert.True(IsInside(commonDir, root),
            $"git fixture '{repository}' resolves repository '{commonDir}', outside '{root}'.");

        var origin = Git(repository, "config", "--get", "remote.origin.url");
        if (origin.ExitCode != 0) return; // no origin configured: nothing to escape to
        var originPath = Normalize(origin.Output.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
            ? new Uri(origin.Output).LocalPath
            : origin.Output);
        Assert.True(Path.IsPathFullyQualified(originPath) && IsInside(originPath, root),
            $"git fixture '{repository}' has origin '{origin.Output}', outside '{root}'.");
    }

    private static (int ExitCode, string Output) Git(string repository, params string[] arguments)
    {
        using var process = Process.Start(GitProcessStartInfoFactory.Create(repository, arguments))
                            ?? throw new InvalidOperationException("Could not start git.");
        var output = process.StandardOutput.ReadToEnd().Trim();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }

    private static bool IsInside(string path, string root) =>
        path.Equals(root, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string path) =>
        Path.GetFullPath(path).Replace('\\', '/').TrimEnd('/');
}
