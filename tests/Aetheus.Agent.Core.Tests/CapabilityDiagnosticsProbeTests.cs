// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Agent.Core.Services;
using NSubstitute;

namespace Aetheus.Agent.Core.Tests;

/// <summary>
/// T13: the capability diagnostics drive the operator-facing "WHY is this capability off" hints
/// (S-FEAT-HBDX / S-TECH-CAPX). Exercises the Linux-only branch through the internal seam
/// (isWindows: false + test-owned sudoers paths) so the unreadable-drop-in and sudo-probe
/// contracts are frozen from the Windows test host. Previously untested.
/// </summary>
public class CapabilityDiagnosticsProbeTests
{
    private static ShellExecResult SudoOk() => new(0, "User may run the following commands", string.Empty);
    private static ShellExecResult SudoBlocked() => new(1, string.Empty, "sudo: a password is required");

    [Fact]
    public async Task CollectAsync_OnWindows_ReturnsEmpty_AndNeverProbes()
    {
        var shell = Substitute.For<IShellRunner>();

        var diagnostics = await CapabilityDiagnosticsProbe.CollectAsync(
            shell, isWindows: true, sudoersFiles: ["/etc/sudoers.d/aetheus-agent"], TestContext.Current.CancellationToken);

        Assert.Empty(diagnostics);
        await shell.DidNotReceiveWithAnyArgs()
            .RunExecAsync(default!, default!, TestContext.Current.CancellationToken, default);
    }

    [Fact]
    public async Task CollectAsync_SudoWorks_NoSudoersFiles_ReturnsEmpty()
    {
        var shell = Substitute.For<IShellRunner>();
        shell.RunExecAsync("sudo", Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>(), Arg.Any<TimeSpan?>())
            .Returns(SudoOk());

        var diagnostics = await CapabilityDiagnosticsProbe.CollectAsync(
            shell, isWindows: false, sudoersFiles: ["/nonexistent/aetheus-agent"], TestContext.Current.CancellationToken);

        Assert.Empty(diagnostics);
        // The real-execution probe is exactly `sudo -n -l` (S-TECH-CAPX).
        await shell.Received(1).RunExecAsync(
            "sudo",
            Arg.Is<IReadOnlyList<string>>(a => a.SequenceEqual(new[] { "-n", "-l" })),
            Arg.Any<CancellationToken>(),
            Arg.Any<TimeSpan?>());
    }

    [Fact]
    public async Task CollectAsync_SudoProbeFails_ReportsSandboxBlock()
    {
        var shell = Substitute.For<IShellRunner>();
        shell.RunExecAsync("sudo", Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>(), Arg.Any<TimeSpan?>())
            .Returns(SudoBlocked());

        var diagnostics = await CapabilityDiagnosticsProbe.CollectAsync(
            shell, isWindows: false, sudoersFiles: [], TestContext.Current.CancellationToken);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Contains("passwordless sudo unavailable", diagnostic);
    }

    [Fact]
    public async Task CollectAsync_SudoBinaryMissing_ReportsProbeCouldNotRun()
    {
        var shell = Substitute.For<IShellRunner>();
        shell.RunExecAsync("sudo", Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>(), Arg.Any<TimeSpan?>())
            .Returns<ShellExecResult>(_ => throw new System.ComponentModel.Win32Exception("file not found"));

        var diagnostics = await CapabilityDiagnosticsProbe.CollectAsync(
            shell, isWindows: false, sudoersFiles: [], TestContext.Current.CancellationToken);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Contains("probe could not run", diagnostic);
    }

    [Fact]
    public async Task CollectAsync_SudoersFilePresentButUnreadable_ReportsMissingReadAcl()
    {
        const string unreadableFile = "/etc/sudoers.d/aetheus-agent";
        var shell = Substitute.For<IShellRunner>();
        shell.RunExecAsync("sudo", Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>(), Arg.Any<TimeSpan?>())
            .Returns(SudoOk());

        var diagnostics = await CapabilityDiagnosticsProbe.CollectAsync(
            shell,
            isWindows: false,
            sudoersFiles: [unreadableFile],
            TestContext.Current.CancellationToken,
            fileExists: path => path == unreadableFile,
            readAllBytesAsync: (_, _) => Task.FromException<byte[]>(new UnauthorizedAccessException("denied")));

        var diagnostic = Assert.Single(diagnostics);
        Assert.Contains("present but unreadable", diagnostic);
        Assert.Contains(Path.GetFileName(unreadableFile), diagnostic);
    }
}
