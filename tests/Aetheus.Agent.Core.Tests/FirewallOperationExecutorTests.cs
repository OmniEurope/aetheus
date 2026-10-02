// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Agent.Core.Executors;
using Aetheus.Agent.Core.Operations;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aetheus.Agent.Core.Tests;

public class FirewallOperationExecutorTests
{
    private static readonly Func<string, TaskLogLevel, Task> NoOutput = (_, _) => Task.CompletedTask;

    private static FirewallOperationExecutor Build()
        => new(NullLogger<FirewallOperationExecutor>.Instance);

    [Fact]
    public void CanHandle_OnlyFirewallKinds()
    {
        var exec = Build();
        Assert.True(exec.CanHandle(OperationKind.FirewallAllow));
        Assert.True(exec.CanHandle(OperationKind.FirewallDeny));
        Assert.True(exec.CanHandle(OperationKind.FirewallDeleteRule));
        Assert.True(exec.CanHandle(OperationKind.FirewallSetEnabled));
        Assert.False(exec.CanHandle(OperationKind.SystemPackageUpgrade));
    }

    [Fact]
    public async Task InvalidPort_RejectedBeforeAnyPrivilegedCall()
    {
        // Port out of range fails the shared target validator - refused regardless of OS, no sudo attempted.
        var result = await Build().ExecuteAsync(OperationKind.FirewallAllow, "99999", 60, NoOutput, TestContext.Current.CancellationToken);
        Assert.Equal(-1, result.ExitCode);
    }

    [Fact]
    public async Task InvalidToggleTarget_Rejected()
    {
        var result = await Build().ExecuteAsync(OperationKind.FirewallSetEnabled, "nonsense", 60, NoOutput, TestContext.Current.CancellationToken);
        Assert.Equal(-1, result.ExitCode);
    }

    [Fact]
    public void HelperPath_IsTheRootOwnedHelper()
        => Assert.Equal("/usr/local/lib/aetheus/aetheus-firewall", FirewallOperationExecutor.HelperPath);
}
