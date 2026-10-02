// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Tests.Helpers;

/// <summary>
/// "X ago" must not depend on how a timestamp reached the page. REST values arrive local, SignalR
/// values stay UTC; an agent updated six minutes earlier read "2h 6m ago" in Paris summer time once a
/// heartbeat replaced the REST value. The same instant must read the same, in either kind.
/// </summary>
public sealed class RelativeTimeTests
{
    private static readonly BunitTestHelper.StubLocalizer L = new();

    [Fact]
    public void TheSameInstant_ReadsTheSame_WhetherItArrivedUtcOrLocal()
    {
        var utc = DateTime.UtcNow.AddMinutes(-6);

        Assert.Equal(RelativeTime.FormatAgo(L, utc.ToLocalTime()), RelativeTime.FormatAgo(L, utc));
        Assert.Equal("ContactAgentMinutesAgo", RelativeTime.FormatAgo(L, utc));
    }

    [Fact]
    public void ALocalValue_KeepsReadingAgainstTheLocalClock()
    {
        Assert.Equal("ContactAgentMinutesAgo", RelativeTime.FormatAgo(L, DateTime.Now.AddMinutes(-6)));
        Assert.Equal("ContactAgentHoursAgo", RelativeTime.FormatAgo(L, DateTime.Now.AddHours(-3)));
    }
}
