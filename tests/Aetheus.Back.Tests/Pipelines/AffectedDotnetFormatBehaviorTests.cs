// SPDX-License-Identifier: EUPL-1.2
using System.Diagnostics;
using Aetheus.Back.Components.Git;
using Aetheus.Back.Tests.Git;

namespace Aetheus.Back.Tests.Pipelines;

public sealed class AffectedDotnetFormatBehaviorTests
{
    [Theory]
    [InlineData("modified", "affected")]
    [InlineData("renamed", "full")]
    [InlineData("deleted", "full")]
    [InlineData("configuration", "full")]
    [InlineData("space", "full")]
    [InlineData("missing-baseline", "full")]
    [InlineData("non-ancestor", "full")]
    [InlineData("diff-failure", "full")]
    public async Task Script_SelectsFailClosedModeFromRealGitChanges(string scenario, string expectedMode)
    {
        var shell = ShellPath();

        var root = Directory.CreateTempSubdirectory("aetheus-format-behavior-").FullName;
        try
        {
            await GitAsync(root, "init");
            await GitAsync(root, "config", "user.email", "tests@aetheus.invalid");
            await GitAsync(root, "config", "user.name", "Aetheus Tests");
            GitFixtureGuard.AssertOwnedBy(root, root);
            Directory.CreateDirectory(Path.Combine(root, "src"));
            await File.WriteAllTextAsync(Path.Combine(root, "Aetheus.slnx"), "<Solution />\n",
                TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(root, "src", "App.cs"), "class App { }\n",
                TestContext.Current.CancellationToken);
            await CommitAsync(root, "baseline");
            var baseline = (await GitAsync(root, "rev-parse", "HEAD")).StdOut.Trim();
            var originalBranch = (await GitAsync(root, "branch", "--show-current")).StdOut.Trim();

            switch (scenario)
            {
                case "modified":
                    await File.AppendAllTextAsync(Path.Combine(root, "src", "App.cs"), "// changed\n",
                        TestContext.Current.CancellationToken);
                    break;
                case "renamed":
                    File.Move(Path.Combine(root, "src", "App.cs"), Path.Combine(root, "src", "Renamed.cs"));
                    break;
                case "deleted":
                    File.Delete(Path.Combine(root, "src", "App.cs"));
                    break;
                case "configuration":
                    await File.WriteAllTextAsync(Path.Combine(root, ".editorconfig"), "root = true\n",
                        TestContext.Current.CancellationToken);
                    break;
                case "space":
                    await File.WriteAllTextAsync(Path.Combine(root, "src", "With Space.cs"), "class Spaced { }\n",
                        TestContext.Current.CancellationToken);
                    break;
                case "missing-baseline":
                case "diff-failure":
                    await File.AppendAllTextAsync(Path.Combine(root, "src", "App.cs"), "// changed\n",
                        TestContext.Current.CancellationToken);
                    break;
                case "non-ancestor":
                    await GitAsync(root, "checkout", "--orphan", "unrelated");
                    await GitAsync(root, "rm", "-rf", ".");
                    await File.WriteAllTextAsync(Path.Combine(root, "unrelated.txt"), "unrelated\n",
                        TestContext.Current.CancellationToken);
                    await CommitAsync(root, "unrelated");
                    baseline = (await GitAsync(root, "rev-parse", "HEAD")).StdOut.Trim();
                    await GitAsync(root, "checkout", originalBranch);
                    await File.AppendAllTextAsync(Path.Combine(root, "src", "App.cs"), "// changed\n",
                        TestContext.Current.CancellationToken);
                    break;
            }

            await CommitAsync(root, scenario);
            var head = (await GitAsync(root, "rev-parse", "HEAD")).StdOut.Trim();
            if (scenario == "missing-baseline") baseline = new string('a', 40);

            var fakeBin = Directory.CreateDirectory(Path.Combine(root, "fake-bin")).FullName;
            var dotnetLog = Path.Combine(root, "dotnet.log");
            var fakeDotnet = Path.Combine(fakeBin, "dotnet");
            await File.WriteAllTextAsync(fakeDotnet, """
                #!/bin/sh
                printf '%s\n' "$*" >> "$FAKE_DOTNET_LOG"
                report=""
                previous=""
                for argument in "$@"; do
                  if [ "$previous" = "--report" ]; then report="$argument"; fi
                  previous="$argument"
                done
                if [ -n "$report" ]; then
                  mkdir -p "$report"
                  printf '[]\n' > "$report/format-report.json"
                fi
                """.ReplaceLineEndings("\n"),
                TestContext.Current.CancellationToken);
            var fakeGit = Path.Combine(fakeBin, "git");
            await File.WriteAllTextAsync(fakeGit,
                "#!/bin/sh\nif [ \"${FAKE_GIT_DIFF_FAIL:-}\" = 1 ] && [ \"$1\" = diff ]; then exit 91; fi\nexec \"$REAL_GIT\" \"$@\"\n",
                TestContext.Current.CancellationToken);
            var fakeNode = Path.Combine(fakeBin, "node");
            await File.WriteAllTextAsync(fakeNode, """
                #!/bin/sh
                if [ "$1" = "-e" ]; then printf '[]\n' > "$3"; exit 0; fi
                printf '{}\n' > "$3"
                printf '0\n'
                """.ReplaceLineEndings("\n"), TestContext.Current.CancellationToken);
            SetExecutable(fakeDotnet);
            SetExecutable(fakeGit);
            SetExecutable(fakeNode);

            var result = await RunScriptAsync(root, shell, new Dictionary<string, string>
            {
                ["DOTNET"] = ShellPath(fakeDotnet),
                ["GIT"] = ShellPath(fakeGit),
                ["NODE"] = ShellPath(fakeNode),
                ["FAKE_DOTNET_LOG"] = ShellPath(dotnetLog),
                ["AETHEUS_FORMAT_MODE"] = "affected",
                ["DELIVERY_BASELINE_SOURCE_SHA"] = baseline,
                ["BUILD_SOURCEVERSION"] = head,
                ["REAL_GIT"] = ShellPath(GitExecutable()),
                ["FAKE_GIT_DIFF_FAIL"] = scenario == "diff-failure" ? "1" : "0"
            });

            Assert.True(result.ExitCode == 0,
                $"Format harness failed ({result.ExitCode}). stdout={result.StdOut} stderr={result.StdErr}");
            Assert.Contains($"name=DOTNET_FORMAT_MODE]{expectedMode}", result.StdOut, StringComparison.Ordinal);
            var invocations = await File.ReadAllLinesAsync(dotnetLog, TestContext.Current.CancellationToken);
            Assert.Equal(2, invocations.Length);
            Assert.StartsWith("restore Aetheus.slnx", invocations[0], StringComparison.Ordinal);
            if (expectedMode == "full")
                Assert.DoesNotContain("--include", invocations[1], StringComparison.Ordinal);
            else
                Assert.Contains("--include src/App.cs", invocations[1], StringComparison.Ordinal);
        }
        finally
        {
            DeleteTree(root);
        }
    }

    [Fact]
    public async Task Script_InvalidModeFailsBeforeDotnet()
    {
        var shell = ShellPath();

        var root = Directory.CreateTempSubdirectory("aetheus-format-invalid-").FullName;
        try
        {
            var result = await RunScriptAsync(root, shell, new Dictionary<string, string>
            {
                ["AETHEUS_FORMAT_MODE"] = "invalid"
            });

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("must be affected or full", result.StdErr, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTree(root);
        }
    }

    private static async Task CommitAsync(string root, string message)
    {
        await GitAsync(root, "add", "-A");
        await GitAsync(root, "commit", "-m", message);
    }

    private static async Task<ProcessResult> GitAsync(string root, params string[] arguments)
    {
        var result = await RunAsync(
            GitExecutable(), root, arguments, new Dictionary<string, string>());
        Assert.True(result.ExitCode == 0,
            $"Git failed ({result.ExitCode}): {string.Join(' ', arguments)}\n{result.StdErr}");
        return result;
    }

    private static Task<ProcessResult> RunScriptAsync(
        string root,
        string shell,
        IReadOnlyDictionary<string, string> environment) =>
        RunAsync(
            shell,
            root,
            [ShellPath(Path.Combine(FindRepoRoot(), "deploy", "scripts", "run-affected-dotnet-format.sh"))],
            environment);

    private static async Task<ProcessResult> RunAsync(
        string executable,
        string workingDirectory,
        IReadOnlyCollection<string> arguments,
        IReadOnlyDictionary<string, string> environment)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        foreach (var (key, value) in environment) startInfo.Environment[key] = value;
        // WHY: pre-push hook env inheritance incident - GIT_DIR/GIT_INDEX_FILE would redirect the git
        // calls of these fixture scripts to the real repository.
        GitProcessStartInfoFactory.NeutralizeInheritedGitEnvironment(startInfo);
        using var process = Process.Start(startInfo)!;
        var stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        return new ProcessResult(process.ExitCode, await stdout, await stderr);
    }

    // The format script is a POSIX shell script the pipeline runs; a host that cannot run it cannot
    // exercise this contract, so the lookup fails loudly instead of skipping.
    private static string ShellPath() => ExecutableLocator.Require(
        OperatingSystem.IsWindows() ? "bash" : "sh",
        OperatingSystem.IsWindows() ? @"C:\Program Files\Git\bin\bash.exe" : "/bin/sh");

    private static string GitExecutable() => ExecutableLocator.Require(
        "git",
        OperatingSystem.IsWindows() ? @"C:\Program Files\Git\cmd\git.exe" : "/usr/bin/git");

    private static string ShellPath(string path) => OperatingSystem.IsWindows()
        ? path.Replace('\\', '/')
        : path;

    private static void SetExecutable(string path)
    {
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private static void DeleteTree(string root)
    {
        if (!Directory.Exists(root)) return;
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            try { File.SetAttributes(file, FileAttributes.Normal); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
        Directory.Delete(root, recursive: true);
    }

    private static string FindRepoRoot() => Aetheus.Back.Tests.Architecture.RepositoryScan.Root;

    private sealed record ProcessResult(int ExitCode, string StdOut, string StdErr);
}
