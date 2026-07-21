// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Agent.Core.Collectors;
using Aetheus.Agent.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Aetheus.Agent.Core.Tests;

public class TeamspeakCollectorDetectionTests
{
    // fileExists: _ => false keeps detection hermetic - these tests drive the running-process path via
    // the mocked shell, so the real host filesystem (a CI agent with a ts3server binary on disk) must
    // never leak in and flip "no binary on disk" to installed.
    private static TeamspeakCollector Build(IShellRunner shell) =>
        new(NullLogger<TeamspeakCollector>.Instance, shell, Substitute.For<ITeamspeakQueryClient>(), fileExists: _ => false);

    // Default every shell call to "failure" (which: not found, systemctl: inactive, pidof: none).
    private static IShellRunner Shell()
    {
        var shell = Substitute.For<IShellRunner>();
        shell.RunExecAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>(), Arg.Any<TimeSpan?>())
            .Returns(new ShellExecResult(1, string.Empty, string.Empty));
        return shell;
    }

    // The VPS case: /opt/teamspeak is 0750 teamspeak:teamspeak so File.Exists on the binary fails
    // for the agent, but `pidof ts3server` finds the running process. Detection must still report
    // installed + running so the backend persists a TeamspeakState (port 10011) and exposes the UI.
    [Fact]
    public async Task CollectAsync_RunningButBinaryUnreadable_DetectsInstalledOnDefaultPort()
    {
        var shell = Shell();
        shell.RunExecAsync("pidof", Arg.Is<IReadOnlyList<string>>(a => a.Contains("ts3server")), Arg.Any<CancellationToken>(), Arg.Any<TimeSpan?>())
            .Returns(new ShellExecResult(0, "771", string.Empty));

        var dto = await Build(shell).CollectAsync(TestContext.Current.CancellationToken);

        Assert.True(dto.IsInstalled);
        Assert.True(dto.IsRunning);
        Assert.Equal(10011, dto.QueryPort);
    }

    // The unit is "teamspeak" on this install, not "teamspeak3"; IsRunningAsync must accept both.
    [Fact]
    public async Task CollectAsync_TeamspeakUnitActive_DetectsRunning()
    {
        var shell = Shell();
        shell.RunExecAsync("systemctl", Arg.Is<IReadOnlyList<string>>(a => a.Contains("teamspeak")), Arg.Any<CancellationToken>(), Arg.Any<TimeSpan?>())
            .Returns(new ShellExecResult(0, "active", string.Empty));

        var dto = await Build(shell).CollectAsync(TestContext.Current.CancellationToken);

        Assert.True(dto.IsInstalled);
        Assert.True(dto.IsRunning);
    }

    // No binary on disk and no running process => genuinely not installed.
    [Fact]
    public async Task CollectAsync_NotRunningNoBinary_ReturnsNotInstalled()
    {
        var dto = await Build(Shell()).CollectAsync(TestContext.Current.CancellationToken);

        Assert.False(dto.IsInstalled);
        Assert.False(dto.IsRunning);
    }
}
