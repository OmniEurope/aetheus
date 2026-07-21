// SPDX-License-Identifier: EUPL-1.2
using System.Diagnostics;

namespace Aetheus.Back.Tests.Architecture;

/// <summary>
/// Guards against the "silent .gitignore footgun": a stock Visual Studio rule
/// (<c>[Rr]eleases/</c>, <c>[Ll]ogs/</c>, <c>[Bb]in/</c>…) matches a real source
/// <em>folder</em> by name and silently excludes its <c>.cs</c>/<c>.razor</c> files,
/// so <c>git add -A</c> skips them with no warning. This is exactly how the
/// <c>ReleaseDetail</c> page once vanished from the repo and shipped a prod 404.
///
/// The guard turns that invisible drift into a loud red build: it asks git which
/// tracked-eligible files the ignore rules currently swallow and fails if any of
/// them is a hand-written source file. Generated output (<c>obj/</c>, <c>bin/</c>)
/// is legitimately ignored and excluded from the check.
///
/// Mirrors the on-disk git approach used by the GitLight repo tests - git is a hard
/// build-host dependency, so shelling out to it here is consistent with the suite.
/// </summary>
public sealed class GitignoredSourceAuditTests
{
    [Fact]
    public void No_Source_File_Is_Silently_Gitignored()
    {
        var repoRoot = FindRepoRoot();

        // --others   : untracked files
        // --ignored  : restrict to those an ignore rule matches
        // --exclude-standard : honour .gitignore / .git/info/exclude / global excludes
        var output = RunGit(repoRoot, "ls-files", "--others", "--ignored", "--exclude-standard");

        var offenders = output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(IsHandWrittenSource)
            .Where(p => !IsGeneratedOutput(p))
            .Where(p => !IsNestedWorktree(p))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        Assert.True(offenders.Count == 0,
            "These hand-written source files are silently excluded by a .gitignore rule "
            + "(they will never be committed and can vanish from the repo unnoticed). "
            + "Add a scoped negation (e.g. !**/Pages/Releases/) to .gitignore for each:\n  "
            + string.Join("\n  ", offenders));
    }

    private static bool IsHandWrittenSource(string path) =>
        path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".razor", StringComparison.OrdinalIgnoreCase);

    // Build output is correctly ignored - never flag a generated .cs under obj/ or bin/.
    private static bool IsGeneratedOutput(string path)
    {
        var p = path.Replace('\\', '/');
        return p.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
            || p.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("obj/", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("bin/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNestedWorktree(string path) => path
        .Replace('\\', '/')
        .StartsWith(".claude/worktrees/", StringComparison.OrdinalIgnoreCase);

    private static string RunGit(string workingDir, params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi)
            ?? throw new InvalidOperationException("Could not start git - it must be on PATH on the build host.");
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();

        // A non-zero git exit would leave stdout empty → the audit would "pass" vacuously
        // (false green: it would report no ignored files because git failed, not because the
        // tree is clean). Surface the failure loudly instead.
        if (p.ExitCode != 0)
            throw new InvalidOperationException(
                $"git {string.Join(' ', args)} exited {p.ExitCode}: {stderr.Trim()}");

        return stdout;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(GitignoredSourceAuditTests).Assembly.Location)!);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Aetheus.slnx"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not locate repository root (Aetheus.slnx).");
    }
}
