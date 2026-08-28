// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Configuration;
using Aetheus.Back.Services;

namespace Aetheus.Back.Components.Artifacts;

public sealed class ArtifactCleanupService(
    IServiceScopeFactory scopeFactory,
    ILogger<ArtifactCleanupService> logger,
    TimeProvider timeProvider,
    IPostgresLeaderLease operationLock) : BackgroundService
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

        var deletedCount = 0;
        foreach (var artifact in expired)
        {
            await operationLock.RunSerializedAsync(
                ArtifactRetentionLock.For(artifact.Id),
                async lockToken =>
                {
                    // GetExpiredAsync is only a candidate selection. A checkpoint resume can acquire a
                    // retention lease after that query, so revalidate while holding the same distributed
                    // lock used by resume before deleting any bytes.
                    if (await repo.HasActiveRetentionLeaseAsync(artifact.Id, now, lockToken).ConfigureAwait(false))
                        return;

                    try
                    {
                        await storage.DeleteArtifactAsync(artifact.FilePath, lockToken).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        logger.LogWarning(ex, "Failed to delete artifact file {Path}, skipping DB removal", artifact.FilePath);
                        return;
                    }
                    await repo.RemoveAsync(artifact, lockToken).ConfigureAwait(false);
                    deletedCount++;
                },
                ct).ConfigureAwait(false);
        }

        logger.LogInformation("Artifact cleanup: deleted {Count} expired artifacts", deletedCount);
    }
}

internal static class ArtifactRetentionLock
{
    internal static string For(int artifactId) => $"artifact-retention:{artifactId}";
}
