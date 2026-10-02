// SPDX-License-Identifier: EUPL-1.2
using System.Diagnostics;
using System.Runtime.InteropServices;
using Aetheus.Agent.Core.Operations;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aetheus.Agent.Core.Tests;

[Collection(ProcessSpawningTestCollection.Name)]
public sealed class ProcessRunnerTests
{
    [Fact]
    public async Task RunAsync_SuccessfulStderr_PreservesChannelWithoutClaimingFailure()
    {
        var start = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new ProcessStartInfo("cmd.exe", "/c echo benign 1>&2")
            : new ProcessStartInfo("/bin/sh", "-c \"echo benign >&2\"");
        start.UseShellExecute = false;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        var output = new List<(string Message, TaskLogLevel Level)>();

        var result = await ProcessRunner.RunAsync(
            start,
            timeoutSeconds: 10,
            (message, level) =>
            {
                output.Add((message, level));
                return Task.CompletedTask;
            },
            NullLogger.Instance,
            TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(output, item => item.Message == "[stderr] benign" && item.Level == TaskLogLevel.Warning);
        Assert.Contains(output, item => item.Message == "[process] exit-code=0" && item.Level == TaskLogLevel.Info);
        Assert.DoesNotContain(output, item => item.Level == TaskLogLevel.Error);
    }

    [Fact]
    public async Task RunAsync_NonZeroExit_RecordsSemanticErrorFromExitCode()
    {
        var start = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new ProcessStartInfo("cmd.exe", "/c echo diagnostic 1>&2 & exit /b 7")
            : new ProcessStartInfo("/bin/sh", "-c \"echo diagnostic >&2; exit 7\"");
        start.UseShellExecute = false;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        var output = new List<(string Message, TaskLogLevel Level)>();

        var result = await ProcessRunner.RunAsync(
            start,
            timeoutSeconds: 10,
            (message, level) =>
            {
                output.Add((message, level));
                return Task.CompletedTask;
            },
            NullLogger.Instance,
            TestContext.Current.CancellationToken);

        Assert.Equal(7, result.ExitCode);
        Assert.Contains(output, item => item.Message == "[stderr] diagnostic" && item.Level == TaskLogLevel.Warning);
        Assert.Contains(output, item => item.Message == "[process] exit-code=7" && item.Level == TaskLogLevel.Error);
    }
}
