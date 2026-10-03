// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Configuration;
using Aetheus.Back.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Aetheus.Back.Components.AppBackups;

/// <summary>
/// ADR-024 4.3: fires scheduled backups and periodic restore-checks. Every minute it evaluates each
/// enabled policy's <c>ScheduleCron</c> (dispatch a backup) and <c>RestoreCheckCron</c> (dispatch a
/// restore-check of the latest successful run). Structured like <c>PipelineSchedulerService</c>; due-calc
/// via <see cref="BackupSchedule"/>.
/// </summary>
public sealed class BackupSchedulerService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<BackupSchedulerService> logger,
    IPostgresLeaderLease? leaderLease = null) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = BackendRuntimeDefaults.SchedulerCheckInterval;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(2);

    // Only the live colour runs it (decision of 2026-10-02, PostgresLeaderLease).
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        leaderLease is null
            ? RunLeaderLoopAsync(stoppingToken)
            : leaderLease.RunAsLeaderAsync("aetheus:backup-scheduler", RunLeaderLoopAsync, stoppingToken);

    private async Task RunLeaderLoopAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CheckInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await TickAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Backup scheduler tick failed; will retry next interval");
            }
        }
    }

    // internal (not private) so a unit test can drive one tick deterministically without a PeriodicTimer
    // (BackgroundServiceTestabilityAuditTests). See PipelineSchedulerService / HeartbeatService.
    internal async Task TickAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IBackupRepository>();
        var service = scope.ServiceProvider.GetRequiredService<IBackupPolicyService>();
        var now = timeProvider.GetUtcNow().UtcDateTime;

        foreach (var policy in await repo.GetEnabledPoliciesAsync(ct).ConfigureAwait(false))
        {
            if (BackupSchedule.IsDue(policy.ScheduleCron, now, policy.LastRunAt, Window))
                await service.TriggerBackupAsync(policy, ct).ConfigureAwait(false);

            if (BackupSchedule.IsDue(policy.RestoreCheckCron, now, policy.LastRestoreCheckAt, Window))
            {
                var latest = await repo.GetLatestSuccessfulRunAsync(policy.Id, ct).ConfigureAwait(false);
                if (latest is not null)
                    await service.TriggerRestoreCheckAsync(latest, policy, ct).ConfigureAwait(false);
            }
        }
    }
}
