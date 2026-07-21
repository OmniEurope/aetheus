// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Agent.Core.Configuration;
using Aetheus.Agent.Core.Operations;
using Aetheus.Agent.Core.Services;
using Aetheus.Shared.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Aetheus.Agent.Core.Tests;

public class SystemPackageUpgradeExecutorTests
{
    private static SystemPackageUpgradeExecutor Build()
        => new(Options.Create(new AetheusAgentOptions()), Substitute.For<IShellRunner>(),
               NullLogger<SystemPackageUpgradeExecutor>.Instance);

    [Fact]
    public void CanHandle_OnlySystemPackageUpgrade()
    {
        var exec = Build();
        Assert.True(exec.CanHandle(OperationKind.SystemPackageUpgrade));
        Assert.False(exec.CanHandle(OperationKind.ServiceInstall));
    }

    [Fact]
    public void BuildAptUpgradeArgv_IsArgvExact()
        => Assert.Equal(["-n", "/usr/bin/apt-get", "upgrade", "-y"], SystemPackageUpgradeExecutor.BuildAptUpgradeArgv());
}
