// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Agent.Core.Configuration;
using Aetheus.Agent.Core.Operations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Aetheus.Agent.Core.Tests;

public class PortsentryOperationExecutorTests
{
    private static PortsentryOperationExecutor Build() =>
        new(Options.Create(new AetheusAgentOptions()), NullLogger<PortsentryOperationExecutor>.Instance);

    private static readonly Dictionary<string, string> NoEnv = new();

    private static Dictionary<string, string> Ports(string tcp, string udp) => new(StringComparer.Ordinal)
    {
        [PortsentrySetupEnv.TcpPorts] = tcp,
        [PortsentrySetupEnv.UdpPorts] = udp
    };

    [Theory]
    [InlineData(OperationKind.PortsentryStart, true)]
    [InlineData(OperationKind.PortsentryStop, true)]
    [InlineData(OperationKind.PortsentryRestart, true)]
    [InlineData(OperationKind.PortsentryStatus, true)]
    [InlineData(OperationKind.PortsentryGetLogs, true)]
    [InlineData(OperationKind.PortsentryUnblock, true)]
    [InlineData(OperationKind.PortsentrySetup, true)]
    [InlineData(OperationKind.ServiceInstall, false)]
    [InlineData(OperationKind.CronSave, false)]
    public void CanHandle_OnlyPortsentryKinds(OperationKind kind, bool expected)
        => Assert.Equal(expected, Build().CanHandle(kind));

    [Theory]
    [InlineData("bad mode!", "22,80", "53")]      // invalid mode (space + metachar)
    [InlineData("atcp", "not-ports", "53")]       // invalid TCP port list
    [InlineData("atcp", "22,80", "bad;ports")]    // invalid UDP port list
    [InlineData("atcp", "", "53")]                // empty TCP port list
    public async Task ExecuteAsync_Setup_InvalidParams_ReturnsFailureWithoutSpawning(string mode, string tcp, string udp)
    {
        var messages = new List<string>();
        var result = await Build().ExecuteAsync(
            OperationKind.PortsentrySetup, mode, Ports(tcp, udp), 120,
            (m, _) => { messages.Add(m); return Task.CompletedTask; },
            TestContext.Current.CancellationToken);

        Assert.Equal(-1, result.ExitCode);
        Assert.False(result.TimedOut);
        Assert.Contains(messages, m => m.Contains("Invalid portsentry setup parameters", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExecuteAsync_Setup_MissingPortEnv_ReturnsFailureWithoutSpawning()
    {
        // Valid mode but the port-list env vars are absent - rejected before spawning anything.
        var messages = new List<string>();
        var result = await Build().ExecuteAsync(
            OperationKind.PortsentrySetup, "atcp", NoEnv, 120,
            (m, _) => { messages.Add(m); return Task.CompletedTask; },
            TestContext.Current.CancellationToken);

        Assert.Equal(-1, result.ExitCode);
        Assert.Contains(messages, m => m.Contains("Invalid portsentry setup parameters", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SetupHelperPath_IsTheRootOwnedHelper()
        => Assert.Equal("/usr/local/lib/aetheus/portsentry-setup", PortsentryOperationExecutor.SetupHelperPath);
}
