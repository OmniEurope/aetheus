// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Agent.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aetheus.Agent.Core.Tests;

public sealed class DefaultShellRunnerTests
{
    private readonly DefaultShellRunner _runner = new(NullLogger<DefaultShellRunner>.Instance);

    [Fact]
    public async Task RunAsync_CommandCompletes_ReturnsStdout()
    {
        var output = await _runner.RunAsync("echo aetheus-shell-runner", TestContext.Current.CancellationToken);

        Assert.Contains("aetheus-shell-runner", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunWithStdinAsync_EchoProcess_RoundTripsPayload()
    {
        var (file, args) = StdinEchoCommand();

        var output = await _runner.RunWithStdinAsync(file, args, "payload with spaces\n", TestContext.Current.CancellationToken);

        Assert.Contains("payload with spaces", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunExecAsync_DotnetVersion_ReturnsSeparateSuccessfulChannels()
    {
        var result = await _runner.RunExecAsync("dotnet", ["--version"], TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Matches(@"\d+\.\d+", result.StdOut);
        Assert.True(string.IsNullOrWhiteSpace(result.StdErr));
    }

    [Fact]
    public async Task RunExecAsync_FailingProcess_PreservesExitCodeAndStderr()
    {
        var (file, args) = FailingCommand();

        var result = await _runner.RunExecAsync(file, args, TestContext.Current.CancellationToken);

        Assert.Equal(7, result.ExitCode);
        Assert.Contains("observable-error", result.StdErr, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task PublicMethods_InvalidRequiredArguments_FailFast(int method)
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(async () =>
        {
            if (method == 0)
                await _runner.RunAsync(" ", TestContext.Current.CancellationToken);
            else if (method == 1)
                await _runner.RunWithStdinAsync("", [], "input", TestContext.Current.CancellationToken);
            else
                await _runner.RunExecAsync("", [], TestContext.Current.CancellationToken);
        });
    }

    [Fact]
    public async Task RunExecAsync_Timeout_CancelsAndTerminatesChild()
    {
        var marker = Path.Combine(Path.GetTempPath(), $"aetheus-descendant-{Guid.NewGuid():N}.marker");
        var (file, args) = DescendantMarkerCommand(marker);
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                _runner.RunExecAsync(file, args, TestContext.Current.CancellationToken, TimeSpan.FromMilliseconds(100)));

            await Task.Delay(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
            Assert.False(File.Exists(marker), "The descendant process survived cancellation and wrote its marker.");
        }
        finally
        {
            File.Delete(marker);
        }
    }

    [Fact]
    public async Task RunAsync_Timeout_CancelsAndTerminatesShellTree()
    {
        var command = OperatingSystem.IsWindows()
            ? "ping 127.0.0.1 -n 6 >NUL"
            : "sleep 5";

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _runner.RunAsync(command, TestContext.Current.CancellationToken, TimeSpan.FromMilliseconds(50)));
    }

    [Fact]
    public async Task RunWithStdinAsync_NullArgumentsFailBeforeStartingProcess()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _runner.RunWithStdinAsync("unused", null!, "input", TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _runner.RunWithStdinAsync("unused", [], null!, TestContext.Current.CancellationToken));
    }

    private static (string File, IReadOnlyList<string> Args) StdinEchoCommand() => OperatingSystem.IsWindows()
        ? ("findstr.exe", ["/R", ".*"])
        : ("/bin/cat", []);

    private static (string File, IReadOnlyList<string> Args) FailingCommand() => OperatingSystem.IsWindows()
        ? ("cmd.exe", ["/d", "/s", "/c", "echo observable-error 1>&2 & exit /b 7"])
        : ("/bin/sh", ["-c", "echo observable-error >&2; exit 7"]);

    private static (string File, IReadOnlyList<string> Args) SlowCommand() => OperatingSystem.IsWindows()
        ? ("cmd.exe", ["/d", "/s", "/c", "ping 127.0.0.1 -n 6 >NUL"])
        : ("/bin/sh", ["-c", "sleep 5"]);

    private static (string File, IReadOnlyList<string> Args) DescendantMarkerCommand(string marker)
    {
        if (OperatingSystem.IsWindows())
        {
            var windowsMarker = marker.Replace("'", "''", StringComparison.Ordinal);
            var child = "powershell.exe -NoLogo -NoProfile -NonInteractive -Command " +
                $"\"Start-Sleep -Seconds 1; [IO.File]::WriteAllText('{windowsMarker}', 'child-survived')\"";
            return ("cmd.exe", ["/d", "/s", "/c", child]);
        }

        var escapedMarker = marker.Replace("'", "'\\''", StringComparison.Ordinal);
        return ("/bin/sh", ["-c", $"/bin/sh -c 'sleep 1; printf child-survived > \\\"{escapedMarker}\\\"' & wait"]);
    }
}
