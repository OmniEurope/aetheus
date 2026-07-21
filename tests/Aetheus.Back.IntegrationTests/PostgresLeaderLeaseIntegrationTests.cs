// SPDX-License-Identifier: EUPL-1.2
using System.Diagnostics;
using Aetheus.Back.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Aetheus.Back.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class PostgresLeaderLeaseIntegrationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task TwoInstances_NeverRunTheSameHostedWorkerConcurrently()
    {
        // A leader lease must release its own session lock before returning the connection to the
        // pool. Disable Npgsql's automatic reset so this test cannot pass merely because pool cleanup
        // happens to issue DISCARD ALL / pg_advisory_unlock_all on close.
        var firstConnectionString = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
        {
            NoResetOnClose = true,
            ApplicationName = "aetheus-leader-first"
        }.ConnectionString;
        var secondConnectionString = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
        {
            NoResetOnClose = true,
            ApplicationName = "aetheus-leader-second"
        }.ConnectionString;
        var firstConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = firstConnectionString
            })
            .Build();
        var secondConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = secondConnectionString
            })
            .Build();
        var firstLease = new PostgresLeaderLease(firstConfiguration, NullLogger<PostgresLeaderLease>.Instance);
        var secondLease = new PostgresLeaderLease(secondConfiguration, NullLogger<PostgresLeaderLease>.Instance);
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var firstStop = new CancellationTokenSource();
        using var secondStop = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var first = firstLease.RunAsLeaderAsync("integration:single-leader", async ct =>
        {
            firstEntered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        }, firstStop.Token);
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken);

        var second = secondLease.RunAsLeaderAsync("integration:single-leader", async ct =>
        {
            secondEntered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        }, secondStop.Token);

        // Observe contention continuously across at least one complete 5-second acquisition retry.
        // A short pre-retry delay can pass even when a later retry incorrectly enters concurrently.
        var contentionObservation = Stopwatch.StartNew();
        var contentionChecks = 0;
        do
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), TestContext.Current.CancellationToken);
            Assert.False(
                secondEntered.Task.IsCompleted,
                "The second backend entered while the first still held the PostgreSQL lease.");
            contentionChecks++;
        }
        while (contentionObservation.Elapsed < TimeSpan.FromSeconds(6));

        Assert.True(contentionChecks >= 20, $"Only {contentionChecks} contention observations completed.");

        firstStop.Cancel();
        await first.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken);
        await secondEntered.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken: TestContext.Current.CancellationToken);
        secondStop.Cancel();
        await second.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken);
    }
}
