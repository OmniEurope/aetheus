// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Agent.Core.Services;

internal sealed class AgentLivenessWatchdogService(
    AgentRuntimeHealth health,
    AgentState agentState,
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
    private readonly TimeSpan _restartJitter = TimeSpan.FromSeconds(
        15 + Math.Abs(agentState.ServerId.GetValueOrDefault()) % 30);

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
        var heartbeatAge = Age(now, health.LastHeartbeatSuccessAt);
        var pollingAge = Age(now, health.LastPollingSuccessAt);
        var heartbeatStalled = heartbeatAge > _heartbeatStallThreshold;
        var pollingStalled = pollingAge > _pollingStallThreshold;

        if (heartbeatStalled && pollingStalled)
        {
            logger.LogWarning(
                "Control plane is unreachable: heartbeat and polling have both stalled; preserving the process and retry backoff");
            return false;
        }

        if (!heartbeatStalled && !pollingStalled)
            return false;

        if (!health.IsIdle)
        {
            logger.LogWarning(
                "Local {Loop} loop is stalled but activity remains: tasks={TaskCount}, processes={ProcessCount}; restart deferred",
                heartbeatStalled ? "heartbeat" : "polling",
                health.ActiveTaskCount,
                health.ActiveProcessCount);
            return false;
        }

        var stalledAge = heartbeatStalled ? heartbeatAge!.Value : pollingAge!.Value;
        var threshold = heartbeatStalled ? _heartbeatStallThreshold : _pollingStallThreshold;
        if (stalledAge <= threshold + _restartJitter)
            return false;

        var stalledLoop = heartbeatStalled
            ? $"heartbeat loop ({heartbeatAge!.Value.TotalSeconds:F0}s without a successful heartbeat)"
            : $"polling loop ({pollingAge!.Value.TotalSeconds:F0}s without a successful poll)";
        var reason = $"Aetheus agent self-repair: {stalledLoop} stalled";
        logger.LogCritical(
            "{Reason}; terminating the process so the service manager restarts it",
            reason);
        processRestarter.Restart(reason);
        return true;
    }

    private static TimeSpan? Age(DateTimeOffset now, DateTimeOffset? lastSuccess) =>
        lastSuccess is null ? null : now - lastSuccess.Value;
}
