// SPDX-License-Identifier: EUPL-1.2
using System.Diagnostics;
using Aetheus.Back.Components.Git;

namespace Aetheus.Back.Tests.Architecture;

/// <summary>
/// Guards against the "silent .gitignore footgun": a stock Visual Studio rule
/// (<c>[Rr]eleases/</c>, <c>[Ll]ogs/</c>, <c>[Bb]in/</c>…) matches a real source
/// <em>folder</em> by name and silently excludes its source files,
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
    public void BrowserAnalyticsPackage_RequiredSourcesExist()
    {
        var repoRoot = FindRepoRoot();
        string[] required =
        [
            "packages/aetheus-web-analytics/package.json",
            "packages/aetheus-web-analytics/src/aetheus-web-analytics.js",
            "packages/aetheus-web-analytics/test/aetheus-web-analytics.test.js"
        ];

        var missing = required
            .Where(path => !File.Exists(Path.Combine(repoRoot, path.Replace('/', Path.DirectorySeparatorChar))))
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void No_Source_File_Is_Silently_Gitignored()
    {
        var repoRoot = FindRepoRoot();

        // --others   : untracked files
        // --ignored  : restrict to those an ignore rule matches
        // --exclude-standard : honour .gitignore / .git/info/exclude / global excludes
        // Restrict git's walk to the only two extensions this audit can flag. Scanning every ignored
        // build artifact in a large worktree can take indefinitely while producing data we discard.
        var output = RunGit(repoRoot, "ls-files", "--others", "--ignored", "--exclude-standard",
            "--", ":(glob)**/*.cs", ":(glob)**/*.razor");

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
        || path.EndsWith(".razor", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".proto", StringComparison.OrdinalIgnoreCase)
        || path.Replace('\\', '/').StartsWith(
            "packages/aetheus-web-analytics/",
            StringComparison.OrdinalIgnoreCase)
           && (path.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
               || path.EndsWith(".json", StringComparison.OrdinalIgnoreCase));

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
        // CI and sandboxed local runners may use an identity different from the checkout owner.
        // Scope the trust exception to this exact repository and this process invocation only.
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add($"safe.directory={workingDir.Replace('\\', '/')}");
        foreach (var a in args) psi.ArgumentList.Add(a);
        // WHY: under a git hook (pre-push env inheritance incident) GIT_DIR/GIT_INDEX_FILE would
        // otherwise redirect this git call to the hooking repository instead of workingDir.
        GitProcessStartInfoFactory.NeutralizeInheritedGitEnvironment(psi);

        using var p = Process.Start(psi)
            ?? throw new InvalidOperationException("Could not start git - it must be on PATH on the build host.");
        var stdoutTask = p.StandardOutput.ReadToEndAsync();
        var stderrTask = p.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            p.WaitForExitAsync(timeout.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw new TimeoutException("git ls-files did not complete within 30 seconds.");
        }

        var stdout = stdoutTask.GetAwaiter().GetResult();
        var stderr = stderrTask.GetAwaiter().GetResult();

        // A non-zero git exit would leave stdout empty → the audit would "pass" vacuously
        // (false green: it would report no ignored files because git failed, not because the
        // tree is clean). Surface the failure loudly instead.
        if (p.ExitCode != 0)
            throw new InvalidOperationException(
                $"git {string.Join(' ', args)} exited {p.ExitCode}: {stderr.Trim()}");

        return stdout;
    }

    private static string FindRepoRoot() => Aetheus.Back.Tests.Architecture.RepositoryScan.Root;
}
