// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Agent.Core.Configuration;
using Aetheus.Agent.Core.Services;
using NSubstitute;

namespace Aetheus.Agent.Core.Tests;

public sealed class DeploymentCapabilityProbeTests
{
    [Fact]
    public async Task IsAvailableAsync_ExactVersionedProbeSucceeds()
    {
        var shell = Substitute.For<IShellRunner>();
        shell.RunExecAsync(
                "sudo",
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<TimeSpan?>())
            .Returns(new ShellExecResult(0, DeploymentCapabilityProbe.ProbeResponse, string.Empty));

        var available = await DeploymentCapabilityProbe.IsAvailableAsync(
            shell,
            isWindows: false,
            TestContext.Current.CancellationToken);

        Assert.True(available);
        await shell.Received(1).RunExecAsync(
            "sudo",
            Arg.Is<IReadOnlyList<string>>(args => args.SequenceEqual(
                new[] { "-n", DeploymentCapabilityProbe.HelperPath, "--probe" })),
            Arg.Any<CancellationToken>(),
            AgentRuntimeDefaults.CapabilityProbeTimeout);
    }

    [Theory]
    [InlineData(1, "aetheus-deploy-helper-v2")]
    [InlineData(0, "aetheus-deploy-helper-v1")]
    [InlineData(0, "")]
    public async Task IsAvailableAsync_MissingInvalidOrStaleGrantFailsClosed(int exitCode, string output)
    {
        var shell = Substitute.For<IShellRunner>();
        shell.RunExecAsync(
                "sudo",
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<TimeSpan?>())
            .Returns(new ShellExecResult(exitCode, output, string.Empty));

        var available = await DeploymentCapabilityProbe.IsAvailableAsync(
            shell,
            isWindows: false,
            TestContext.Current.CancellationToken);

        Assert.False(available);
    }
}
