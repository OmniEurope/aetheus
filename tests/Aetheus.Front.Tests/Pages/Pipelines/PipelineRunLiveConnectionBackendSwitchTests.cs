// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Components.Pipelines;
using Aetheus.Front.Tests.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aetheus.Front.Tests.Pages.Pipelines;

/// <summary>
/// Run 2458: after the blue-green Switch of aetheus-deploy-prod the run page's socket stayed on the
/// previous colour and the page stopped following the run. The new-version signal must restart that
/// same connection and reload the run once.
/// </summary>
public class PipelineRunLiveConnectionBackendSwitchTests
{
    [Fact]
    public async Task WhenTheBackendIsReplaced_TheSameConnectionRestartsAndTheRunReloadsOnce()
    {
        var auth = new AuthStateProvider(new FakeJsRuntime(), NullLogger<AuthStateProvider>.Instance);
        var factory = new SilentHubConnectionFactory(auth);
        var reloads = 0;
        var reloaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var connection = new PipelineRunLiveConnection(
            factory, NullLogger.Instance, 2458, () => [],
            () => { Interlocked.Increment(ref reloads); reloaded.TrySetResult(); return Task.CompletedTask; },
            () => Task.CompletedTask,
            callback => callback());
        await connection.StartAsync();
        var hub = factory.Connection!;
        Assert.Equal(1, hub.Starts);

        factory.AnnounceBackendReplaced();
        await reloaded.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        // Let a stray second reload surface before counting (the reload is debounced by 250 ms).
        await Task.Delay(500, TestContext.Current.CancellationToken);

        Assert.Equal(1, factory.Created);
        Assert.Equal(2, hub.Starts);
        Assert.Equal(1, Volatile.Read(ref reloads));

        await connection.DisposeAsync();
        factory.AnnounceBackendReplaced();
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.Equal(2, hub.Starts);
    }
}
