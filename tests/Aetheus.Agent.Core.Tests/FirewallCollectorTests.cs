// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Agent.Core.Collectors;
using Aetheus.Agent.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Aetheus.Agent.Core.Tests;

public class FirewallCollectorTests
{
    private const string ActiveStatus = """
        Status: active

        [ 1] 22/tcp                     ALLOW IN    Anywhere
        [ 2] 8080/tcp                   ALLOW IN    10.0.0.0/8
        """;

    private static FirewallCollector Build(IShellRunner shell, bool ufwPresent = true)
        => new(NullLogger<FirewallCollector>.Instance, shell, _ => ufwPresent);

    [Fact]
    public async Task NoUfw_ReportsNotInstalled()
    {
        var result = await Build(Substitute.For<IShellRunner>(), ufwPresent: false).CollectAsync(TestContext.Current.CancellationToken);

        Assert.False(result.Installed);
        Assert.False(result.Active);
    }

    [Fact]
    public async Task HelperReadSucceeds_ReportsActiveAndRules()
    {
        var shell = Substitute.For<IShellRunner>();
        shell.RunExecAsync("sudo", Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>(), Arg.Any<TimeSpan?>())
            .Returns(new ShellExecResult(0, ActiveStatus, ""));

        var result = await Build(shell).CollectAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Installed);
        Assert.True(result.Active);
        Assert.True(result.StatusKnown);
        Assert.Empty(result.CollectionDiagnostics);
        Assert.Equal(2, result.Rules.Count);
    }

    [Fact]
    public async Task NoGrant_ReportsInstalledOnly_NotFakeInactive()
    {
        var shell = Substitute.For<IShellRunner>();
        shell.RunExecAsync("sudo", Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>(), Arg.Any<TimeSpan?>())
            .Returns(new ShellExecResult(1, "", "sudo: a password is required"));

        var result = await Build(shell).CollectAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Installed);      // ufw binary present
        Assert.False(result.StatusKnown);
        Assert.False(result.Active);
        Assert.Empty(result.Rules);
        Assert.Contains("could not be read", result.CollectionDiagnostics, StringComparison.Ordinal);
    }
}
