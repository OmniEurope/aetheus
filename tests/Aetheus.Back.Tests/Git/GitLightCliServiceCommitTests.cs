// SPDX-License-Identifier: EUPL-1.2
using System.Diagnostics;
using Aetheus.Back.Components.Git;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aetheus.Back.Tests.Git;

// Exercises CommitFileAsync against a REAL temporary bare git repo (git must be on PATH).
public sealed class GitLightCliServiceCommitTests : IDisposable
{
    private readonly string _bare = Path.Combine(Path.GetTempPath(), $"prom-git-test-{Guid.NewGuid():N}.git");
    private readonly GitLightCliService _svc;

    public GitLightCliServiceCommitTests()
    {
        var runner = new GitProcessRunner(NullLogger<GitProcessRunner>.Instance);
        _svc = new GitLightCliService(
            runner,
            new GitLightCliWriter(runner, NullLogger<GitLightCliWriter>.Instance),
            NullLogger<GitLightCliService>.Instance,
            TimeProvider.System);
        RunGit($"init --bare --initial-branch main \"{_bare}\"");
    }

    [Fact]
    public async Task CommitFileAsync_EmptyRepo_CreatesAndReadsBack()
    {

        var (ok, sha, error) = await _svc.CommitFileAsync(
            _bare, "main", ".pipeline/ci.yaml", "name: ci\nstages: []\n",
            "add ci pipeline", "aetheus", "aetheus@test", ct: TestContext.Current.CancellationToken);

        Assert.True(ok, error);
        Assert.False(string.IsNullOrWhiteSpace(sha));

        var blob = await _svc.GetBlobAsync(_bare, "main", ".pipeline/ci.yaml", ct: TestContext.Current.CancellationToken);
        Assert.NotNull(blob);
        Assert.Contains("name: ci", blob!.Content);
    }

    [Fact]
    public async Task CommitFileAsync_RejectsPathTraversal()
    {

        var (ok, _, error) = await _svc.CommitFileAsync(
            _bare, "main", "../escape.yaml", "x", "msg", "p", "p@test", ct: TestContext.Current.CancellationToken);

        Assert.False(ok);
        Assert.StartsWith("invalid relative path", error);
    }

    [Fact]
    public async Task CommitFileChangesAsync_RenameRemovesOldPathInSameCommit()
    {

        var (seeded, _, seedError) = await _svc.CommitFileAsync(
            _bare, "main", ".pipeline/old.yaml", "name: old\n",
            "seed old", "aetheus", "aetheus@test", ct: TestContext.Current.CancellationToken);
        Assert.True(seeded, seedError);

        var (ok, sha, error) = await _svc.CommitFileChangesAsync(
            _bare, "main", [(".pipeline/new.yaml", "name: new\n")], [".pipeline/old.yaml"],
            "rename pipeline", "aetheus", "aetheus@test", ct: TestContext.Current.CancellationToken);

        Assert.True(ok, error);
        Assert.False(string.IsNullOrWhiteSpace(sha));
        Assert.Null(await _svc.GetBlobAsync(_bare, "main", ".pipeline/old.yaml", ct: TestContext.Current.CancellationToken));
        Assert.Contains("name: new", (await _svc.GetBlobAsync(
            _bare, "main", ".pipeline/new.yaml", ct: TestContext.Current.CancellationToken))!.Content);
    }

    [Fact]
    public async Task GetCommitMessagesAsync_ReadsSeveralMessagesWithOneGitOperation()
    {

        var (firstOk, firstSha, firstError) = await _svc.CommitFileAsync(
            _bare, "main", "one.txt", "one", "feat: first message", "aetheus", "aetheus@test",
            ct: TestContext.Current.CancellationToken);
        Assert.True(firstOk, firstError);
        var (secondOk, secondSha, secondError) = await _svc.CommitFileAsync(
            _bare, "main", "two.txt", "two", "fix: second message", "aetheus", "aetheus@test",
            ct: TestContext.Current.CancellationToken);
        Assert.True(secondOk, secondError);

        var messages = await _svc.GetCommitMessagesAsync(
            _bare, [firstSha!, secondSha!], TestContext.Current.CancellationToken);

        Assert.Equal("feat: first message", messages[firstSha!]);
        Assert.Equal("fix: second message", messages[secondSha!]);
    }

    // git is a hard prerequisite of the product, not an optional convenience: a host without it must
    // fail these tests rather than quietly pass a suite that exercised nothing.
    private static void RunGit(string args)
    {
        var git = ExecutableLocator.Require("git");
        var psi = new ProcessStartInfo(git, args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        using var process = Process.Start(psi)
                            ?? throw new InvalidOperationException($"Could not start '{git}'.");
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(10000);
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git {args} failed ({process.ExitCode}): {stderr}");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_bare))
            {
                foreach (var f in Directory.EnumerateFiles(_bare, "*", SearchOption.AllDirectories))
                {
                    var a = File.GetAttributes(f);
                    if ((a & FileAttributes.ReadOnly) != 0) File.SetAttributes(f, a & ~FileAttributes.ReadOnly);
                }
                Directory.Delete(_bare, true);
            }
        }
        catch { /* best effort */ }
    }
}
