// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Configuration;
using Aetheus.Back.Services;

namespace Aetheus.Back.Components.Artifacts;

public sealed class ArtifactCleanupService(
    IServiceScopeFactory scopeFactory,
    ILogger<ArtifactCleanupService> logger,
    TimeProvider timeProvider,
    IPostgresLeaderLease operationLock,
    IChunkedArtifactUploadService chunkedUploads) : BackgroundService
{
    /// <summary>R-463: one backend cleans at a time; both blue-green colours used to select and delete
    /// the same expired artifacts.</summary>
    internal const string LeaseName = "aetheus:artifact-cleanup";

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        operationLock.RunAsLeaderAsync(LeaseName, RunLeaderLoopAsync, stoppingToken);

    private async Task RunLeaderLoopAsync(CancellationToken stoppingToken)
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

    /// <summary>How long an unfinished chunked upload keeps its parts on disk. A transfer of a
    /// gigabyte-scale artifact can legitimately span a long run, so the window is generous; what it
    /// bounds is the abandoned session, whose parts nothing else would ever delete.</summary>
    internal static readonly TimeSpan UnfinishedUploadLifetime = TimeSpan.FromHours(24);

    internal async Task CleanupExpiredArtifactsAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IArtifactRepository>();
        var storage = scope.ServiceProvider.GetRequiredService<IArtifactStorageService>();
        var now = timeProvider.GetUtcNow().UtcDateTime;

        PurgeAbandonedUploads();

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
                    if (await repo.RemoveAsync(artifact, lockToken).ConfigureAwait(false))
                        deletedCount++;
                },
                ct).ConfigureAwait(false);
        }

        logger.LogInformation("Artifact cleanup: deleted {Count} expired artifacts", deletedCount);
    }

    /// <summary>
    /// Parts of uploads that were begun and never completed. Best-effort and never fatal: an
    /// unreadable session directory must not stop the artifact cleanup that runs beside it.
    /// </summary>
    private void PurgeAbandonedUploads()
    {
        try
        {
            var purged = chunkedUploads.PurgeExpired(UnfinishedUploadLifetime);
            if (purged > 0)
                logger.LogInformation("Artifact cleanup: removed {Count} abandoned chunked uploads", purged);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Failed to purge abandoned chunked uploads");
        }
    }
}

internal static class ArtifactRetentionLock
{
    internal static string For(int artifactId) => $"artifact-retention:{artifactId}";
}
