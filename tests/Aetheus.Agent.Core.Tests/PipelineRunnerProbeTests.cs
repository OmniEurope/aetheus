// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Agent.Core.Services;
using NSubstitute;

namespace Aetheus.Agent.Core.Tests;

/// <summary>
/// The pipeline-runner capability probe drives the backend's per-server PipelineRunnerEnabled gate,
/// so its "both dotnet AND git must run" logic must not regress (a false positive authorises a box
/// to receive steps it cannot execute). Previously untested.
/// </summary>
public class PipelineRunnerProbeTests
{
    private static ShellExecResult Ok(string version = "1.2.3") => new(0, version, string.Empty);
    private static ShellExecResult Fail() => new(127, string.Empty, "not found");

    [Fact]
    public async Task IsAvailableAsync_DotnetAndGitPresent_ReturnsTrue()
    {
        var shell = Substitute.For<IShellRunner>();
        shell.RunExecAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>(), Arg.Any<TimeSpan?>())
            .Returns(Ok());

        Assert.True(await PipelineRunnerProbe.IsAvailableAsync(shell, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task IsAvailableAsync_DotnetMissing_ReturnsFalse()
    {
        var shell = Substitute.For<IShellRunner>();
        // Every dotnet candidate fails; git would pass - but the AND requires both.
        shell.RunExecAsync(Arg.Is<string>(f => f.Contains("dotnet")), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>(), Arg.Any<TimeSpan?>())
            .Returns(Fail());
        shell.RunExecAsync(Arg.Is<string>(f => f.Contains("git")), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>(), Arg.Any<TimeSpan?>())
            .Returns(Ok());

        Assert.False(await PipelineRunnerProbe.IsAvailableAsync(shell, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task IsAvailableAsync_GitMissing_ReturnsFalse()
    {
        var shell = Substitute.For<IShellRunner>();
        shell.RunExecAsync(Arg.Is<string>(f => f.Contains("dotnet")), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>(), Arg.Any<TimeSpan?>())
            .Returns(Ok());
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
        // A binary that exits 0 but prints nothing is not a real toolchain hit.
        shell.RunExecAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>(), Arg.Any<TimeSpan?>())
            .Returns(new ShellExecResult(0, "   ", string.Empty));

        Assert.False(await PipelineRunnerProbe.IsAvailableAsync(shell, TestContext.Current.CancellationToken));
    }
}
