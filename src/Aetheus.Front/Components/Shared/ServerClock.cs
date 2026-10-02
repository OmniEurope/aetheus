// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Front.Components.Shared;

/// <summary>
/// The backend's clock, as seen from this browser.
///
/// Every duration on a running entity is the difference between a timestamp the server wrote and a
/// "now" the browser reads, and the two clocks are not the same clock. A browser running twenty
/// seconds behind subtracts a start time that is still in its future, and the negative result is
/// clamped to zero: a build that has been running for fifteen seconds shows "0s" until the browser
/// catches up. Measured on 2026-09-06 against production: -19.9 s.
///
/// So durations use <see cref="UtcNow"/> instead of <c>DateTime.UtcNow</c>. Until the first API
/// response has been seen the offset is zero, which is exactly the old behaviour.
/// </summary>
public static class ServerClock
{
    /// <summary>How far ahead of this browser the server's clock runs. Zero until known.</summary>
    public static TimeSpan Offset { get; private set; } = TimeSpan.Zero;

    /// <summary>UTC now on the server's clock, as the browser can best tell.</summary>
    public static DateTime UtcNow => DateTime.UtcNow + Offset;

    /// <summary>
    /// Records the offset from one API response's <c>Date</c> header. Half the round trip is charged
    /// to the response leg, the usual first-order correction; anything finer would need a real time
    /// protocol, and would not change a twenty-second skew.
    /// </summary>
    public static void Observe(DateTimeOffset serverDate, TimeSpan roundTrip)
        => Offset = serverDate.UtcDateTime + (roundTrip / 2) - DateTime.UtcNow;

    /// <summary>Forgets the observed offset. Tests only.</summary>
    internal static void Reset() => Offset = TimeSpan.Zero;
}
