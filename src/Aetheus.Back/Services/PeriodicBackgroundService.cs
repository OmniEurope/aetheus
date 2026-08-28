// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Services;

public abstract class PeriodicBackgroundService(TimeSpan interval) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await ExecuteIterationAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LogIterationError(ex);
            }
        }
    }

    protected abstract Task ExecuteIterationAsync(CancellationToken ct);

    protected abstract void LogIterationError(Exception exception);
}
