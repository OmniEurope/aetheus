// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Resources;
using Microsoft.Extensions.Localization;

namespace Aetheus.Front.Helpers;

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

    /// <summary>"Just now" / "2m ago" relative to <see cref="DateTime.Now"/>.</summary>
    /// <remarks>
    /// Assumes the input is in browser-local time (the convention enforced by
    /// the global UTC→Local JSON converter in <c>Services/JsonOptions.cs</c>).
    /// </remarks>
    public static string FormatAgo(IStringLocalizer<AppStrings> L, DateTime localTime)
    {
        if (localTime.Year < 2000) return L["Never"];
        return FormatAgo(L, DateTime.Now - localTime);
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
