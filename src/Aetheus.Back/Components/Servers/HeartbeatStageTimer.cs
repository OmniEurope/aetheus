// SPDX-License-Identifier: EUPL-1.2
using System.Globalization;

namespace Aetheus.Back.Components.Servers;

/// <summary>
/// R-459: times each stage of one heartbeat's processing, so a slow heartbeat (6.3 s once, unexplained
/// by the code alone) says where the time went. Nothing is written for a heartbeat faster than
/// <see cref="SlowThreshold"/>; a slower one writes one line with every stage's duration, at Information
/// up to <see cref="WarningThreshold"/> and at Warning from there (recette R-479: beats of 2 to 7 s are
/// common while a host compiles or deploys, and are not by themselves something to act on).
/// </summary>
internal sealed class HeartbeatStageTimer(TimeProvider time)
{
    public static readonly TimeSpan SlowThreshold = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan WarningThreshold = TimeSpan.FromSeconds(10);

    private readonly long _start = time.GetTimestamp();
    private readonly List<(string Stage, TimeSpan Duration)> _stages = [];
    private long? _last;

    /// <summary>Closes the stage that ran since the previous mark (or since the start).</summary>
    public void Mark(string stage)
    {
        var now = time.GetTimestamp();
        _stages.Add((stage, time.GetElapsedTime(_last ?? _start, now)));
        _last = now;
    }

    public TimeSpan Total => time.GetElapsedTime(_start);

    public IReadOnlyList<(string Stage, TimeSpan Duration)> Stages => _stages;

    public void LogIfSlow(ILogger logger, int serverId)
    {
        var total = Total;
        if (total < SlowThreshold) return;
        logger.Log(
            total >= WarningThreshold ? LogLevel.Warning : LogLevel.Information,
            "Heartbeat of server {ServerId} took {TotalMs} ms: {Stages}",
            serverId,
            Math.Round(total.TotalMilliseconds),
            string.Join(", ", _stages.Select(stage => string.Create(
                CultureInfo.InvariantCulture, $"{stage.Stage} {Math.Round(stage.Duration.TotalMilliseconds)} ms"))));
    }
}
