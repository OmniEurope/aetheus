// SPDX-License-Identifier: EUPL-1.2
using System.Diagnostics;

namespace Aetheus.Agent.Core.Tests;

[Collection(ProcessSpawningTestCollection.Name)]
public sealed class DockerImageRetentionBehaviorTests
{
    [Fact]
    public async Task DryRun_PreservesOrderProtectedAndNMinusOneWithoutRemoving()
    {
        var shell = RequireShell();
        using var harness = new RetentionHarness(shell);

        var result = await harness.RunAsync(dryRun: true);

        Assert.True(
            result.ExitCode == 0,
            $"retention exited {result.ExitCode}{Environment.NewLine}stdout:{Environment.NewLine}{result.StdOut}{Environment.NewLine}stderr:{Environment.NewLine}{result.StdErr}");
        AssertInOrder(
            result.StdOut,
            "KEEP protected managed:active sha-protected",
            "KEEP recent managed:previous sha-previous",
            "WOULD_REMOVE managed:old sha-old");
        Assert.DoesNotContain("image rm", await File.ReadAllTextAsync(
            harness.DockerLog,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task InspectFailure_FailsClosedWithoutRemoving()
    {
        var shell = RequireShell();
        using var harness = new RetentionHarness(shell);

        var result = await harness.RunAsync(dryRun: false, inspectFailure: true);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("refusing image retention", result.StdErr, StringComparison.Ordinal);
        Assert.DoesNotContain("image rm", await File.ReadAllTextAsync(
            harness.DockerLog,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task VanishedContainer_IsSkippedWithoutWeakeningRetention()
    {
        var shell = RequireShell();
        using var harness = new RetentionHarness(shell);

        var result = await harness.RunAsync(dryRun: false, inspectFailure: true, containerVanished: true);

        Assert.True(
            result.ExitCode == 0,
            $"retention exited {result.ExitCode}{Environment.NewLine}stdout:{Environment.NewLine}{result.StdOut}{Environment.NewLine}stderr:{Environment.NewLine}{result.StdErr}");
        Assert.Contains("SKIP vanished container container-1", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("image rm managed:old", await File.ReadAllTextAsync(
            harness.DockerLog,
            TestContext.Current.CancellationToken));
    }

    private static void AssertInOrder(string value, params string[] fragments)
    {
        var previous = -1;
        foreach (var fragment in fragments)
        {
            var current = value.IndexOf(fragment, StringComparison.Ordinal);
            Assert.True(current > previous, $"Expected '{fragment}' after offset {previous}.{Environment.NewLine}{value}");
            previous = current;
        }
    }

    // retain-docker-images.sh ships with the agent and runs on every managed host, so a machine that
    // cannot execute a POSIX shell fails here instead of quietly dropping the retention contract.
    private static string RequireShell() => ExecutableLocator.Require(
        "sh",
        OperatingSystem.IsWindows() ? @"C:\Program Files\Git\usr\bin\sh.exe" : "/bin/sh");

    private sealed class RetentionHarness : IDisposable
    {
        private readonly string _shell;
        private readonly string _root;
        private readonly string _bin;
        private readonly string _script;
        internal string DockerLog { get; }

        internal RetentionHarness(string shell)
        {
            _shell = shell;
            _root = Path.Combine(Path.GetTempPath(), $"retention-{Guid.NewGuid():N}");
            _bin = Path.Combine(_root, "bin");
            _script = ToShellPath(Path.Combine(
                FindRepoRoot(),
                "deploy",
                "scripts",
                "retain-docker-images.sh"));
            DockerLog = Path.Combine(_root, "docker.log");
            Directory.CreateDirectory(_bin);
            File.WriteAllText(DockerLog, string.Empty);
            var flock = Path.Combine(_bin, "flock");
            var docker = Path.Combine(_bin, "docker");
            File.WriteAllText(flock, "#!/bin/sh\nexit 0\n");
            File.WriteAllText(docker, """
                #!/bin/sh
                printf '%s\n' "$*" >> "$AETHEUS_FAKE_DOCKER_LOG"
                if [ "$1" = ps ]; then
                    case "$*" in
                        *--filter*)
                            [ "${AETHEUS_FAKE_CONTAINER_VANISHED:-0}" = 1 ] && exit 0
                            ;;
                    esac
                    printf '%s\n' container-1
                    exit 0
                fi
                if [ "$1" = inspect ]; then
                    [ "${AETHEUS_FAKE_INSPECT_FAIL:-0}" = 1 ] && exit 9
                    printf '%s\n' sha-protected
                    exit 0
                fi
                if [ "$1" = image ] && [ "$2" = ls ]; then
                    case "$*" in
                        *CreatedAt*)
                            printf '%s\n' \
                                'sha-protected|managed:active|2026-07-28' \
                                'sha-previous|managed:previous|2026-07-27' \
                                'sha-old|managed:old|2026-07-26'
                            ;;
                        *)
                            printf '%s\n' \
                                'sha-protected|managed:active' \
                                'sha-previous|managed:previous' \
                                'sha-old|managed:old'
                            ;;
                    esac
                    exit 0
                fi
                if [ "$1" = image ] && [ "$2" = inspect ]; then
                    case "$5" in
                        managed:previous) printf '%s\n' sha-previous ;;
                        managed:old) printf '%s\n' sha-old ;;
                        *) exit 8 ;;
                    esac
                    exit 0
                fi
                if [ "$1" = image ] && [ "$2" = rm ]; then
                    exit 0
                fi
                exit 7
                """.ReplaceLineEndings("\n"));
            if (!OperatingSystem.IsWindows())
            {
                const UnixFileMode executable =
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
                File.SetUnixFileMode(flock, executable);
                File.SetUnixFileMode(docker, executable);
            }
        }

        internal async Task<ProcessResult> RunAsync(
            bool dryRun,
            bool inspectFailure = false,
            bool containerVanished = false)
        {
            var psi = new ProcessStartInfo
            {
                FileName = _shell,
                WorkingDirectory = FindRepoRoot(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add("PATH=\"$1:$2\"; export PATH; shift 2; exec \"$@\"");
            psi.ArgumentList.Add("retention-harness");
            psi.ArgumentList.Add(ToShellPath(_bin));
            psi.ArgumentList.Add(OperatingSystem.IsWindows()
                ? ToShellPath(Path.GetDirectoryName(_shell)!)
                : "/usr/bin:/bin");
            psi.ArgumentList.Add(ToShellPath(_shell));
            psi.ArgumentList.Add(_script);
            psi.ArgumentList.Add("managed");
            psi.Environment["TMPDIR"] = _root;
            psi.Environment["AETHEUS_IMAGE_RETENTION_KEEP"] = "2";
            psi.Environment["AETHEUS_IMAGE_RETENTION_DRY_RUN"] = dryRun ? "true" : "false";
            psi.Environment["AETHEUS_FAKE_DOCKER_LOG"] = DockerLog;
            psi.Environment["AETHEUS_FAKE_INSPECT_FAIL"] = inspectFailure ? "1" : "0";
            psi.Environment["AETHEUS_FAKE_CONTAINER_VANISHED"] = containerVanished ? "1" : "0";

            using var process = Process.Start(psi)!;
            var stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            await process.WaitForExitAsync(TestContext.Current.CancellationToken);
            return new ProcessResult(process.ExitCode, await stdout, await stderr);
        }

        private static string ToShellPath(string path)
        {
            if (!OperatingSystem.IsWindows()) return path;
            var full = Path.GetFullPath(path).Replace('\\', '/');
            return $"/{char.ToLowerInvariant(full[0])}{full[2..]}";
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }

        private static string FindRepoRoot() => Aetheus.Agent.Core.Tests.RepositoryScan.Root;
    }

    private sealed record ProcessResult(int ExitCode, string StdOut, string StdErr);
}
