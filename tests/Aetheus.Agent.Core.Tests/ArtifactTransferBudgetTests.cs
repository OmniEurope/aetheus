// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Agent.Core.Operations;

namespace Aetheus.Agent.Core.Tests;

public sealed class ArtifactTransferBudgetTests
{
    [Fact]
    public async Task LightTransfersRemainParallel_WhileHeavyTransferWaitsForTheWave()
    {
        var budget = new ArtifactTransferBudget();
        await using var first = await budget.AcquireAsync(1024, TestContext.Current.CancellationToken);
        await using var second = await budget.AcquireAsync(1024, TestContext.Current.CancellationToken);
        var heavy = budget.AcquireAsync(512L * 1024 * 1024, TestContext.Current.CancellationToken);

        Assert.False(heavy.IsCompleted);
        await first.DisposeAsync();
        Assert.False(heavy.IsCompleted);
        await second.DisposeAsync();
        await using var admitted = await heavy.WaitAsync(
            TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
    }
}
