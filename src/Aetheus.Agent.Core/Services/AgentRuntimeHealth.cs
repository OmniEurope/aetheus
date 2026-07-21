// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Agent.Core.Services;

public sealed class AgentRuntimeHealth(TimeProvider timeProvider)
{
    private long _lastHeartbeatProgressUtcTicks;
    private long _lastPollingProgressUtcTicks;

    public void MarkHeartbeatProgress() =>
        Interlocked.Exchange(ref _lastHeartbeatProgressUtcTicks, timeProvider.GetUtcNow().UtcTicks);

    public void MarkPollingProgress() =>
        Interlocked.Exchange(ref _lastPollingProgressUtcTicks, timeProvider.GetUtcNow().UtcTicks);

    public DateTimeOffset? LastHeartbeatProgressAt => Read(ref _lastHeartbeatProgressUtcTicks);

    public DateTimeOffset? LastPollingProgressAt => Read(ref _lastPollingProgressUtcTicks);

    private static DateTimeOffset? Read(ref long ticks)
    {
        var value = Volatile.Read(ref ticks);
        return value == 0 ? null : new DateTimeOffset(value, TimeSpan.Zero);
    }
}
