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
    private static readonly TimeSpan FastRetryDelay = TimeSpan.FromMilliseconds(100);

    [Fact]
    public async Task SerializedOperation_BlocksASecondBackendUntilTheFirstReleases()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = fixture.ConnectionString
            })
            .Build();
        var firstLease = new PostgresLeaderLease(configuration, NullLogger<PostgresLeaderLease>.Instance);
        var secondLease = new PostgresLeaderLease(configuration, NullLogger<PostgresLeaderLease>.Instance);
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var first = firstLease.RunSerializedAsync(
            "integration:serialized-operation",
            async ct =>
            {
                firstEntered.SetResult();
                await releaseFirst.Task.WaitAsync(ct);
            },
            timeout.Token);
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), timeout.Token);

        var second = secondLease.RunSerializedAsync(
            "integration:serialized-operation",
            _ =>
            {
                secondEntered.SetResult();
                return Task.CompletedTask;
            },
            timeout.Token);

        await Task.Delay(TimeSpan.FromMilliseconds(500), timeout.Token);
        Assert.False(secondEntered.Task.IsCompleted);

        releaseFirst.SetResult();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5), timeout.Token);
        Assert.True(secondEntered.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task LeaderWorkerFailure_ReleasesLeaseAndRestartsWithoutEscaping()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = fixture.ConnectionString
            })
            .Build();
        var lease = new PostgresLeaderLease(
            configuration,
            NullLogger<PostgresLeaderLease>.Instance,
            FastRetryDelay);
        var secondAttemptEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var attempts = 0;

        var run = lease.RunAsLeaderAsync("integration:worker-recovery", async ct =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
                throw new InvalidOperationException("simulated run-specific reconciliation failure");

            secondAttemptEntered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        }, stop.Token);

        await secondAttemptEntered.Task.WaitAsync(
            TimeSpan.FromSeconds(12),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, attempts);
        Assert.False(run.IsCompleted, "The supervised worker must remain active after its first failure.");

        stop.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken);
    }

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
        var secondLease = new PostgresLeaderLease(
            secondConfiguration,
            NullLogger<PostgresLeaderLease>.Instance,
            FastRetryDelay);
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

        // Observe contention continuously across several complete acquisition retries. The test-only
        // retry interval preserves the production control flow without spending six wall-clock seconds.
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
        while (contentionObservation.Elapsed < TimeSpan.FromMilliseconds(450));

        Assert.True(contentionChecks >= 2, $"Only {contentionChecks} contention observations completed.");

        firstStop.Cancel();
        await first.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken);
        await secondEntered.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken: TestContext.Current.CancellationToken);
        secondStop.Cancel();
        await second.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken);
    }
}
