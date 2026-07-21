// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Resources;
using Microsoft.Extensions.Localization;
using Radzen;

namespace Aetheus.Front.Helpers;

/// <summary>
/// Shared "why is this server offline?" reason text and badge severity. After
/// the global UTC→Local JSON converter, every <c>DateTime</c> coming back from
/// the API is in the browser's LOCAL timezone - so the elapsed-time calculation
/// must use <see cref="DateTime.Now"/>, not <c>UtcNow</c>. A simple year
/// threshold spots the "never reported" case robustly (MinValue gets shifted by
/// local offset on the wire conversion, so an exact <c>== default</c> check is
/// unreliable).
/// </summary>
internal static class ServerHeartbeatHelper
{
    // Severity only ever grades an OFFLINE badge (see Severity), so green is never an option.
    // Heartbeat cadence is 30 s. We treat:
    //   < StaleDangerSeconds   → Warning ("transient gap, recently lost")
    //   ≥ StaleDangerSeconds   → Danger ("dead-agent territory")
    // This means a brief flap doesn't look the same as a 5-minute outage.
    public const int StaleDangerSeconds = 300;

    public static string OfflineReason(IStringLocalizer<AppStrings> L, DateTime lastHeartbeat)
    {
        if (lastHeartbeat.Year < 2000) return L["ServerOfflineNeverReported"];
        var span = DateTime.Now - lastHeartbeat;
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        return string.Format(L["ServerOfflineNoHeartbeat"], RelativeTime.FormatAgo(L, span));
    }

    /// <summary>
    /// Severity to color-grade the <b>offline</b> badge by staleness. Both call sites invoke this
    /// only when <c>Status != Online</c>, so it must never return <see cref="BadgeStyle.Success"/>
    /// (a green "Offline" badge is a contradiction, the very bug this guards against). A recently
    /// lost agent is Warning (yellow, "transient gap"); a long-dead or never-seen agent is Danger (red).
    /// </summary>
    public static BadgeStyle Severity(DateTime lastHeartbeat)
    {
        if (lastHeartbeat.Year < 2000) return BadgeStyle.Danger;
        var seconds = (DateTime.Now - lastHeartbeat).TotalSeconds;
        return seconds < StaleDangerSeconds ? BadgeStyle.Warning : BadgeStyle.Danger;
    }

    // Back-compat shim: existing callers used ServerHeartbeatHelper.FormatAgo.
    // New code should call RelativeTime.FormatAgo directly.
    public static string FormatAgo(IStringLocalizer<AppStrings> L, TimeSpan span) =>
        RelativeTime.FormatAgo(L, span);
}
