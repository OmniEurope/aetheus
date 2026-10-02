// SPDX-License-Identifier: EUPL-1.2
using System.Diagnostics;
using Aetheus.Back.Components.Git;

namespace Aetheus.Back.Tests.Git;

/// <summary>
/// Regression for the 2026-09-17 incident: a pre-push hook launched from a linked worktree left a
/// repository-local git variable (at least the common directory) in the test environment. The fixtures
/// neutralized only GIT_DIR/GIT_INDEX_FILE/GIT_WORK_TREE/GIT_PREFIX, so their "temporary" repositories
/// resolved the real one, rewrote its branches and force-pushed a fixture commit to the production
/// origin's main. These tests run real git against a decoy repository standing in for the hooking one.
/// </summary>
public sealed class GitProcessStartInfoFactoryTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("aetheus-git-env-").FullName;

    public void Dispose()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception) { /* best effort: a leaked temp dir is harmless */ }
    }

    [Fact]
    public void NeutralizeInheritedGitEnvironment_InheritedRepositoryOfAHook_ResolvesTheWorkingDirectoryRepository()
    {
        var hooking = InitRepository("hooking");
        var target = InitRepository("target");
        var hookingGitDir = Path.Combine(hooking, ".git");

        var startInfo = GitStartInfo(target, "rev-parse", "--path-format=absolute", "--git-common-dir", "--git-dir");
        // What a git hook running in a linked worktree can hand down to every child process.
        startInfo.Environment["GIT_DIR"] = hookingGitDir;
        startInfo.Environment["GIT_COMMON_DIR"] = hookingGitDir;
        startInfo.Environment["GIT_OBJECT_DIRECTORY"] = Path.Combine(hookingGitDir, "objects");
        startInfo.Environment["GIT_INDEX_FILE"] = Path.Combine(hookingGitDir, "index");

        GitProcessStartInfoFactory.NeutralizeInheritedGitEnvironment(startInfo);
        var resolved = Run(startInfo).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var expected = Normalize(Path.Combine(target, ".git"));
        Assert.Equal([expected, expected], resolved.Select(Normalize));
    }

    [Fact]
    public void NeutralizeInheritedGitEnvironment_RemovesEveryRepositoryLocalVariableGitDeclares()
    {
        // git itself is the authority on which variables are repository-local: the list must track it.
        var declared = Run(GitStartInfo(_root, "rev-parse", "--local-env-vars"))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Contains("GIT_COMMON_DIR", declared);

        var startInfo = GitStartInfo(_root, "version");
        foreach (var name in declared) startInfo.Environment[name] = "poisoned";
        // GIT_CONFIG_COUNT enumerates numbered companions that --local-env-vars does not list.
        startInfo.Environment["GIT_CONFIG_KEY_0"] = "remote.origin.url";
        startInfo.Environment["GIT_CONFIG_VALUE_0"] = "https://example.invalid/prod.git";

        GitProcessStartInfoFactory.NeutralizeInheritedGitEnvironment(startInfo);

        foreach (var name in declared) Assert.DoesNotContain(name, startInfo.Environment.Keys);
        Assert.DoesNotContain("GIT_CONFIG_KEY_0", startInfo.Environment.Keys);
        Assert.DoesNotContain("GIT_CONFIG_VALUE_0", startInfo.Environment.Keys);
    }

    private string InitRepository(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        Run(GitProcessStartInfoFactory.Create(path, ["init", "--quiet"]));
        return path;
    }

    private static ProcessStartInfo GitStartInfo(string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        return startInfo;
    }

    private static string Run(ProcessStartInfo startInfo)
    {
        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"git {string.Join(' ', startInfo.ArgumentList)} failed: {error}");
        return output;
    }

    private static string Normalize(string path) =>
        Path.GetFullPath(path).Replace('\\', '/').TrimEnd('/').ToLowerInvariant();
}
