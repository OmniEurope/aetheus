// SPDX-License-Identifier: EUPL-1.2
using Bunit;

namespace Aetheus.Front.Tests.Shared;

/// <summary>
/// Behavioural coverage for <see cref="HeartbeatPulse"/> - shipped with real logic and no test:
/// the online/offline/disabled state switch, the "never reported" branch (<c>Year &lt; 2000 -> "-"</c>),
/// the alive countdown to the next beat, the four <c>FormatCompact</c> staleness buckets (offline),
/// and the <c>IDisposable</c> ticker lifecycle.
/// The stub localizer returns each key verbatim, so asserting on the key proves the right branch ran.
/// </summary>
public class HeartbeatPulseTests : BunitContext
{
    public HeartbeatPulseTests()
    {
        BunitTestHelper.RegisterServices(this);
    }

    private static ServerDetailDto Server(ServerStatus status, DateTime lastBeat) =>
        new() { Status = status, LastHeartbeat = lastBeat };

    [Theory]
    [InlineData(ServerStatus.Online, "heartbeat-pulse-online", "ServerAlive")]
    [InlineData(ServerStatus.Offline, "heartbeat-pulse-offline", "Offline")]
    [InlineData(ServerStatus.Disabled, "heartbeat-pulse-disabled", "Disabled")]
    public void Renders_state_class_and_label_for_each_status(ServerStatus status, string cssClass, string labelKey)
    {
        var cut = Render<HeartbeatPulse>(p => p
            .Add(x => x.Server, Server(status, DateTime.Now.AddSeconds(-10))));

        Assert.Contains(cssClass, cut.Markup);
        Assert.Equal(labelKey, cut.Find(".heartbeat-pulse-state").TextContent.Trim());
    }

    [Fact]
    public void Never_reported_heartbeat_renders_dash()
    {
        // A default DateTime (Year 1) is the "never reported" sentinel - must read as "-", not a
        // huge elapsed duration.
        var cut = Render<HeartbeatPulse>(p => p
            .Add(x => x.Server, Server(ServerStatus.Disabled, default)));

        Assert.Equal("-", cut.Find(".heartbeat-pulse-duration").TextContent.Trim());
    }

    [Theory]
    [InlineData(5, "DurationSecondsShort")]      // < 60 s
    [InlineData(300, "DurationMinutesShort")]    // 5 min
    [InlineData(18000, "DurationHoursShort")]    // 5 h
    [InlineData(432000, "DurationDaysShort")]    // 5 d
    public void FormatCompact_picks_the_right_duration_bucket(double secondsAgo, string expectedKey)
    {
        // FormatCompact (staleness of the last signal) drives the centre only when the server is NOT
        // alive - an offline/disabled ring is static, so it shows how long ago the last beat was.
        var cut = Render<HeartbeatPulse>(p => p
            .Add(x => x.Server, Server(ServerStatus.Offline, DateTime.Now.AddSeconds(-secondsAgo))));

        Assert.Contains(expectedKey, cut.Find(".heartbeat-pulse-duration").TextContent);
    }

    [Fact]
    public void Online_renders_countdown_to_next_beat()
    {
        // Alive: the centre is a live countdown to the next expected beat (30 → 0), not the elapsed
        // staleness. 10 s into the 30 s cadence leaves ~20 s, formatted via DurationSecondsShort
        // regardless of how the elapsed time would have bucketed under FormatCompact.
        var cut = Render<HeartbeatPulse>(p => p
            .Add(x => x.Server, Server(ServerStatus.Online, DateTime.Now.AddSeconds(-10))));

        Assert.Contains("DurationSecondsShort", cut.Find(".heartbeat-pulse-duration").TextContent);
    }

    [Fact]
    public void Dispose_stops_the_ticker_without_throwing()
    {
        var cut = Render<HeartbeatPulse>(p => p
            .Add(x => x.Server, Server(ServerStatus.Online, DateTime.Now.AddSeconds(-3))));

        var ex = Record.Exception(() => cut.Instance.Dispose());

        Assert.Null(ex);
    }
}
