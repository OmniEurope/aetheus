// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Services.DomainEvents;

/// <summary>
/// Continuously drains <see cref="IBackgroundTaskQueue"/>, creating a fresh DI scope per item
/// so resolved services (DbContext, repositories, etc.) are isolated.
/// </summary>
public sealed class BackgroundTaskQueueHostedService(
    IBackgroundTaskQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<BackgroundTaskQueueHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            Func<IServiceProvider, CancellationToken, Task> work;
            try
            {
                work = await queue.DequeueAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return; }

            await ProcessOneAsync(work, stoppingToken).ConfigureAwait(false);
        }
    }

    internal async Task ProcessOneAsync(Func<IServiceProvider, CancellationToken, Task> work, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        try
        {
            await work(scope.ServiceProvider, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Background queued work failed");
        }
        finally
        {
            queue.IncrementProcessed();
        }
    }
}
