// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Git;
using Aetheus.Back.Configuration;
using Microsoft.Extensions.Options;

namespace Aetheus.Back.Components.ExternalRepos;

/// <summary>
/// Scheduled refresh of external-repo mirrors. A no-op while <c>Features:ExternalRepos</c> is off.
/// Wakes once a minute and fetches every auto-sync connection whose fetch interval has elapsed.
/// </summary>
public sealed class ExternalRepoSyncService(
    IServiceScopeFactory scopeFactory,
    IOptions<FeatureFlagsOptions> features,
    TimeProvider timeProvider,
    ILogger<ExternalRepoSyncService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = BackendRuntimeDefaults.SchedulerCheckInterval;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            if (!features.Value.ExternalRepos)
            {
                continue;
            }

            try
            {
                await SyncDueMirrorsAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[ExternalRepos] scheduled sync sweep failed");
            }
        }
    }

    internal async Task SyncDueMirrorsAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var lightRepo = scope.ServiceProvider.GetRequiredService<IGitLightRepository>();
        var gitRepo = scope.ServiceProvider.GetRequiredService<IGitRepository>();
        var mirror = scope.ServiceProvider.GetRequiredService<IExternalRepoMirrorService>();
        var notifier = scope.ServiceProvider.GetRequiredService<IEntityChangeNotifier>();

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var mirrors = await lightRepo.GetAllAsync(ct).ConfigureAwait(false);

        foreach (var mirrorRepo in mirrors)
        {
            if (mirrorRepo.GitConnectionId is null)
            {
                continue;
            }

            var connection = await gitRepo.FindConnectionAsync(mirrorRepo.GitConnectionId.Value, ct).ConfigureAwait(false);
            if (connection is null || !connection.AutoSyncEnabled || connection.MirrorStatus == GitMirrorStatus.Syncing)
            {
                continue;
            }

            var interval = TimeSpan.FromMinutes(Math.Clamp(connection.FetchIntervalMinutes, 1, 1440));
            var due = connection.LastFetchedAt is null || now - connection.LastFetchedAt.Value >= interval;
            if (!due)
            {
                continue;
            }

            await mirror.SyncAsync(connection, mirrorRepo, ct).ConfigureAwait(false);
            await gitRepo.SaveChangesAsync(ct).ConfigureAwait(false);
            await lightRepo.SaveChangesAsync(ct).ConfigureAwait(false);
            await notifier.BroadcastAsync(ResourceType.Project, connection.ProjectId, EntityChangeOps.Updated, ct).ConfigureAwait(false);
        }
    }
}
