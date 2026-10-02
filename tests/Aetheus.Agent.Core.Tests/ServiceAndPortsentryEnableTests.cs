// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Agent.Core.Configuration;
using Aetheus.Agent.Core.Operations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Aetheus.Agent.Core.Tests;

// Phase 3: ServiceEnable joins the service executor; the portsentry unblock now routes through a
// root-owned helper instead of in-executor sudo iptables/sed.
public class ServiceAndPortsentryEnableTests
{
    private static ServiceOperationExecutor BuildService() =>
        new(Options.Create(new AetheusAgentOptions()), NullLogger<ServiceOperationExecutor>.Instance);

    private static PortsentryOperationExecutor BuildPortsentry() =>
        new(Options.Create(new AetheusAgentOptions()), NullLogger<PortsentryOperationExecutor>.Instance);

    [Fact]
    public void ServiceExecutor_HandlesServiceEnable()
    {
        var sut = BuildService();
        Assert.True(sut.CanHandle(OperationKind.ServiceEnable));
        Assert.True(sut.CanHandle(OperationKind.ServiceStart));
        Assert.False(sut.CanHandle(OperationKind.CronSave));
    }

    [Fact]
    public void PortsentryExecutor_UnblockHelperPath_IsRootOwnedLibLocation()
        => Assert.Equal("/usr/local/lib/aetheus/unblock-ip", PortsentryOperationExecutor.UnblockHelperPath);

    // #11: Linux service verbs run as `sudo -n /bin/systemctl <verb> <unit>` (matching the argv-exact
    // AETHEUS_SYSTEMCTL allow-list), not bare `systemctl` which is a no-op for the non-root agent.
    [Theory]
    [InlineData(OperationKind.ServiceStart, "start")]
    [InlineData(OperationKind.ServiceStop, "stop")]
    [InlineData(OperationKind.ServiceRestart, "restart")]
    [InlineData(OperationKind.ServiceStatus, "status")]
    public void BuildLinuxSystemctlArgv_RoutesThroughSudoSystemctl(OperationKind kind, string verb)
    {
        var argv = ServiceOperationExecutor.BuildLinuxSystemctlArgv(kind, "apache2");
        Assert.Equal(new[] { "-n", "/bin/systemctl", verb, "apache2" }, argv);
    }

    [Fact]
    public void BuildLinuxSystemctlArgv_StripsDotServiceSuffix_ToMatchAllowList()
    {
        var argv = ServiceOperationExecutor.BuildLinuxSystemctlArgv(OperationKind.ServiceRestart, "nginx.service");
        Assert.Equal(new[] { "-n", "/bin/systemctl", "restart", "nginx" }, argv);
    }

    [Fact]
    public void BuildLinuxSystemctlArgv_NonServiceVerb_ReturnsNull()
        => Assert.Null(ServiceOperationExecutor.BuildLinuxSystemctlArgv(OperationKind.ServiceEnable, "apache2"));
}
