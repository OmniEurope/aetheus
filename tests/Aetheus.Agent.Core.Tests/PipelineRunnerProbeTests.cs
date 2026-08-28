// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Agent.Core.Services;
using NSubstitute;

namespace Aetheus.Agent.Core.Tests;

/// <summary>
/// The pipeline-runner capability probe drives the backend's per-server PipelineRunnerEnabled gate,
/// so its Git workspace-preparation contract must not regress. Application SDKs are deliberately
/// absent from this host probe and are validated in locked containers.
/// </summary>
public class PipelineRunnerProbeTests
{
    private static ShellExecResult Ok(string version = "1.2.3") => new(0, version, string.Empty);
    private static ShellExecResult Fail() => new(127, string.Empty, "not found");

    [Fact]
    public async Task IsAvailableAsync_GitPresent_ReturnsTrue()
    {
        var shell = Substitute.For<IShellRunner>();
        shell.RunExecAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>(), Arg.Any<TimeSpan?>())
            .Returns(Ok());

        Assert.True(await PipelineRunnerProbe.IsAvailableAsync(shell, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task IsAvailableAsync_DotnetMissing_GitPresent_ReturnsTrue()
    {
        var shell = Substitute.For<IShellRunner>();
        shell.RunExecAsync(Arg.Is<string>(f => f.Contains("dotnet")), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>(), Arg.Any<TimeSpan?>())
            .Returns(Fail());
        shell.RunExecAsync(Arg.Is<string>(f => f.Contains("git")), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>(), Arg.Any<TimeSpan?>())
            .Returns(Ok());

        Assert.True(await PipelineRunnerProbe.IsAvailableAsync(shell, TestContext.Current.CancellationToken));
        await shell.DidNotReceive().RunExecAsync(
            Arg.Is<string>(file => file.Contains("dotnet", StringComparison.OrdinalIgnoreCase)),
            Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<CancellationToken>(),
            Arg.Any<TimeSpan?>());
    }

    [Fact]
    public async Task IsAvailableAsync_GitMissing_ReturnsFalse()
    {
        var shell = Substitute.For<IShellRunner>();
        shell.RunExecAsync(Arg.Is<string>(f => f.Contains("git")), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>(), Arg.Any<TimeSpan?>())
            .Returns(Fail());

        Assert.False(await PipelineRunnerProbe.IsAvailableAsync(shell, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task IsAvailableAsync_BinaryThrows_TreatedAsAbsent()
    {
        var shell = Substitute.For<IShellRunner>();
        shell.RunExecAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>(), Arg.Any<TimeSpan?>())
            .Returns<ShellExecResult>(_ => throw new System.ComponentModel.Win32Exception("file not found"));

        Assert.False(await PipelineRunnerProbe.IsAvailableAsync(shell, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task IsAvailableAsync_ExitZeroButEmptyStdout_TreatedAsAbsent()
    {
        var shell = Substitute.For<IShellRunner>();
        // A binary that exits 0 but prints nothing is not a real capability hit.
        shell.RunExecAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>(), Arg.Any<TimeSpan?>())
            .Returns(new ShellExecResult(0, "   ", string.Empty));

        Assert.False(await PipelineRunnerProbe.IsAvailableAsync(shell, TestContext.Current.CancellationToken));
    }
}
