// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Auth;

namespace Aetheus.Back.Services;

public sealed class RefreshTokenCleanupService(
    IServiceScopeFactory scopeFactory,
    ILogger<RefreshTokenCleanupService> logger,
    TimeProvider timeProvider) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(6));

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await CleanupExpiredTokensAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during refresh token cleanup");
            }
        }
    }

    internal async Task CleanupExpiredTokensAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAuthRepository>();
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var deleted = await repo.DeleteAllExpiredRefreshTokensAsync(now, ct).ConfigureAwait(false);

        if (deleted > 0)
            logger.LogInformation("Purged {Count} expired/revoked refresh tokens", deleted);
    }
}
