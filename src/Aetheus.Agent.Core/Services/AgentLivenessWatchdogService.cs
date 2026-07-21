// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Agent.Core.Configuration;
using Microsoft.Extensions.Options;

namespace Aetheus.Agent.Core.Services;

internal sealed class AgentLivenessWatchdogService(
    AgentRuntimeHealth health,
    IEnrollmentService enrollment,
    IAgentProcessRestarter processRestarter,
    TimeProvider timeProvider,
    IOptions<AetheusAgentOptions> options,
    ILogger<AgentLivenessWatchdogService> logger) : BackgroundService
{
    private readonly TimeSpan _heartbeatStallThreshold = TimeSpan.FromSeconds(Math.Max(
        90,
        (options.Value.HeartbeatIntervalSeconds * 2)
        + options.Value.HeartbeatCollectionTimeoutSeconds
        + 15));
    private readonly TimeSpan _pollingStallThreshold = TimeSpan.FromSeconds(Math.Max(
        90,
        (options.Value.PollingIntervalSeconds * 3) + 30));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(AgentRuntimeDefaults.LivenessCheckInterval, timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            if (CheckAndRestartIfStalled())
                return;
        }
    }

    internal bool CheckAndRestartIfStalled()
    {
        if (!enrollment.IsEnrolled)
            return false;

        var now = timeProvider.GetUtcNow();
        var heartbeatAge = Age(now, health.LastHeartbeatProgressAt);
        var pollingAge = Age(now, health.LastPollingProgressAt);

        var stalledLoop = heartbeatAge > _heartbeatStallThreshold
            ? $"heartbeat loop ({heartbeatAge.Value.TotalSeconds:F0}s without progress)"
            : pollingAge > _pollingStallThreshold
                ? $"polling loop ({pollingAge.Value.TotalSeconds:F0}s without progress)"
                : null;

        if (stalledLoop is null)
            return false;

        var reason = $"Aetheus agent self-repair: {stalledLoop} stalled";
        logger.LogCritical(
            "{Reason}; terminating the process so the service manager restarts it",
            reason);
        processRestarter.Restart(reason);
        return true;
    }

    private static TimeSpan? Age(DateTimeOffset now, DateTimeOffset? lastProgress) =>
        lastProgress is null ? null : now - lastProgress.Value;
}
