// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Agent.Core.Operations;

namespace Aetheus.Agent.Core.Tests;

[Collection(ProcessSpawningTestCollection.Name)]
public sealed class ScannerResourceBudgetTests
{
    [Theory]
    [InlineData("512m", 536870912)]
    [InlineData("2g", 2147483648)]
    [InlineData("1.5gb", 1610612736)]
    public void ParseMemory_UsesBinaryResourceUnits(string value, long expected)
    {
        Assert.Equal(expected, ScannerResourceBudget.ParseMemory(value));
    }

    [Fact]
    public async Task AcquireAsync_AllowsGitleaksBesideOneHeavyScanner_ButQueuesSecondHeavy()
    {
        var budget = new ScannerResourceBudget(
            ScannerResourceBudget.CandidateMemoryBytes,
            ScannerResourceBudget.CandidateCpus);
        var heavy = new ScannerManifestEntry { Key = "heavy", Memory = "2g", Cpus = "2" };
        var light = new ScannerManifestEntry { Key = "gitleaks", Memory = "512m", Cpus = "1" };
        await using var firstHeavy = await budget.AcquireAsync(heavy, TestContext.Current.CancellationToken);
        await using var gitleaks = await budget.AcquireAsync(light, TestContext.Current.CancellationToken);
        var secondHeavy = budget.AcquireAsync(heavy, TestContext.Current.CancellationToken);

        Assert.False(secondHeavy.IsCompleted);
        await firstHeavy.DisposeAsync();
        await using var admitted = await secondHeavy.WaitAsync(
            TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
    }
}
