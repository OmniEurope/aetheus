// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Agent.Core.Configuration;
using Aetheus.Agent.Core.Operations;
using Aetheus.Shared.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Aetheus.Agent.Core.Tests;

public class CronOperationExecutorTests
{
    private static CronOperationExecutor Build() =>
        new(Options.Create(new AetheusAgentOptions()), NullLogger<CronOperationExecutor>.Instance);

    [Fact]
    public void CanHandle_OnlyCronSaveAndDelete()
    {
        var sut = Build();
        Assert.True(sut.CanHandle(OperationKind.CronSave));
        Assert.True(sut.CanHandle(OperationKind.CronDelete));
        Assert.False(sut.CanHandle(OperationKind.ServiceStart));
        Assert.False(sut.CanHandle(OperationKind.PortsentryUnblock));
    }

    // The helper path the executor invokes is the security boundary deposited by the install script.
    [Fact]
    public void HelperPath_IsRootOwnedLibLocation()
        => Assert.Equal("/usr/local/lib/aetheus/cron-apply", CronOperationExecutor.HelperPath);
}
