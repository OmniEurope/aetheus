// SPDX-License-Identifier: EUPL-1.2
using System.Diagnostics;
using Aetheus.Back.Components.Git;
using Aetheus.Back.Tests.Git;

namespace Aetheus.Back.Tests.Pipelines;

public sealed class PipelineGitFetchIntegrationTests
{
    [Fact]
    public void FetchAllHeadsDepthOne_MakesNonDefaultBranchTipReachable()
    {
        var root = Path.Combine(Path.GetTempPath(), $"aetheus-git-fetch-{Guid.NewGuid():N}");
        var work = Path.Combine(root, "work");
        var origin = Path.Combine(root, "origin.git");
        var clone = Path.Combine(root, "clone");
        Directory.CreateDirectory(root);

        try
        {
            Git(root, "init", "--bare", origin);
            Git(root, "init", "-b", "main", work);
            Git(work, "config", "user.name", "Aetheus Test");
            Git(work, "config", "user.email", "test@aetheus.invalid");
            File.WriteAllText(Path.Combine(work, "base.txt"), "base");
            Git(work, "add", "base.txt");
            Git(work, "commit", "-m", "base");
            Git(work, "remote", "add", "origin", origin);
            GitFixtureGuard.AssertOwnedBy(work, root);
            Git(work, "push", "-u", "origin", "main");

            Git(work, "checkout", "-b", "release/1");
            File.WriteAllText(Path.Combine(work, "release.txt"), "release");
            Git(work, "add", "release.txt");
            Git(work, "commit", "-m", "release");
            var releaseSha = Git(work, "rev-parse", "HEAD").Trim();
            Git(work, "push", "origin", "release/1");

            var originUri = new Uri(origin + Path.DirectorySeparatorChar).AbsoluteUri;
            Git(root, "clone", "--branch", "main", "--single-branch", "--depth", "1", originUri, clone);
            Assert.NotEqual(0, GitExitCode(clone, "cat-file", "-e", $"{releaseSha}^{{commit}}"));

            Git(clone, "fetch", "--depth", "1", "origin", "+refs/heads/*:refs/remotes/origin/*");

            Assert.Equal(0, GitExitCode(clone, "cat-file", "-e", $"{releaseSha}^{{commit}}"));
        }
        finally
        {
            DeleteGitTree(root);
        }
    }

    private static string Git(string workingDirectory, params string[] arguments)
    {
        var (exitCode, output, error) = RunGit(workingDirectory, arguments);
        Assert.True(exitCode == 0,
            $"git {string.Join(' ', arguments)} failed with {exitCode}: {error}");
        return output;
    }

    private static int GitExitCode(string workingDirectory, params string[] arguments)
        => RunGit(workingDirectory, arguments).ExitCode;

    private static void DeleteGitTree(string root)
    {
        if (!Directory.Exists(root)) return;
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            File.SetAttributes(path, FileAttributes.Normal);
        Directory.Delete(root, recursive: true);
    }

    private static (int ExitCode, string Output, string Error) RunGit(
        string workingDirectory, IReadOnlyCollection<string> arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        // WHY: pre-push hook env inheritance incident - GIT_DIR/GIT_INDEX_FILE would redirect this
        // fixture git call to the real repository.
        GitProcessStartInfoFactory.NeutralizeInheritedGitEnvironment(start);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("git did not start");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output, error);
    }
}
