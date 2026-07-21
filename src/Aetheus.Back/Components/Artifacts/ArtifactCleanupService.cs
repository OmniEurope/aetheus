// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Configuration;

namespace Aetheus.Back.Components.Artifacts;

public sealed class ArtifactCleanupService(
    IServiceScopeFactory scopeFactory,
    ILogger<ArtifactCleanupService> logger,
    TimeProvider timeProvider) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(BackendRuntimeDefaults.MaintenanceInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await CleanupExpiredArtifactsAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during artifact cleanup");
            }
        }
    }

    internal async Task CleanupExpiredArtifactsAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IArtifactRepository>();
        var storage = scope.ServiceProvider.GetRequiredService<IArtifactStorageService>();
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var expired = await repo.GetExpiredAsync(now, 100, ct).ConfigureAwait(false);
        if (expired.Count == 0) return;

        foreach (var artifact in expired)
        {
            try
            {
                await storage.DeleteArtifactAsync(artifact.FilePath, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Failed to delete artifact file {Path}, skipping DB removal", artifact.FilePath);
                continue;
            }
            await repo.RemoveAsync(artifact, ct).ConfigureAwait(false);
        }

        logger.LogInformation("Artifact cleanup: deleted {Count} expired artifacts", expired.Count);
    }
}
