// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Shared;

/// <summary>
/// Relative-time formatting ("2m 30s ago", "1h 12m ago", "just now"). Lives
/// outside <see cref="ServerHeartbeatHelper"/> so any grid / log / run cell can
/// reuse it without dragging in heartbeat-specific semantics.
///
/// Pairs naturally with an absolute timestamp tooltip - the label tells the eye
/// "how recent" while the tooltip preserves exact UTC↔local resolution for an
/// operator who needs the wall-clock value.
/// </summary>
internal static class RelativeTime
{
    /// <summary>"Just now" / "2m ago" / "1h 5m ago" from an already-elapsed span.</summary>
    public static string FormatAgo(IStringLocalizer<AppStrings> L, TimeSpan span)
    {
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        if (span.TotalSeconds < 5) return L["JustNow"];
        if (span.TotalSeconds < 60) return string.Format(L["ContactAgentSecondsAgo"], Math.Round(span.TotalSeconds));
        if (span.TotalHours < 1) return string.Format(L["ContactAgentMinutesAgo"], span.Minutes, span.Seconds);
        if (span.TotalDays < 1) return string.Format(L["ContactAgentHoursAgo"], (int)span.TotalHours, span.Minutes);
        return string.Format(L["DaysAgo"], (int)span.TotalDays, span.Hours);
    }

    /// <summary>"Just now" / "2m ago" relative to now, whatever the value's <see cref="DateTimeKind"/>.</summary>
    /// <remarks>
    /// REST payloads arrive in browser-local time (the UTC→Local converter in
    /// <c>Components/Shared/JsonOptions.cs</c>), but SignalR payloads keep the wire's UTC: the hub client has
    /// no such converter. Subtracting a UTC value from <see cref="DateTime.Now"/> added the browser's
    /// UTC offset to the span, so an agent updated six minutes earlier read "2h 6m ago" in Paris
    /// summer time as soon as the first heartbeat replaced the REST value. Each kind is now compared
    /// with the clock of the same kind.
    /// </remarks>
    public static string FormatAgo(IStringLocalizer<AppStrings> L, DateTime time)
    {
        if (time.Year < 2000) return L["Never"];
        var now = time.Kind == DateTimeKind.Utc ? DateTime.UtcNow : DateTime.Now;
        return FormatAgo(L, now - time);
    }

    /// <summary>Future-facing counterpart: "expires in 3 d" / "expires in 5 h" / "expired" relative
    /// to <see cref="DateTime.Now"/>. A sentinel far-future date (year ≥ 9999) reads as "never expires".</summary>
    public static string FormatUntil(IStringLocalizer<AppStrings> L, DateTime localTime)
    {
        if (localTime.Year >= 9999) return L["NeverExpires"];
        var span = localTime - DateTime.Now;
        if (span <= TimeSpan.Zero) return L["Expired"];
        if (span.TotalDays >= 1) return string.Format(L["ExpiresInDays"], (int)span.TotalDays);
        if (span.TotalHours >= 1) return string.Format(L["ExpiresInHours"], (int)span.TotalHours);
        return string.Format(L["ExpiresInMinutes"], Math.Max(1, (int)span.TotalMinutes));
    }
}
