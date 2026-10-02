// SPDX-License-Identifier: EUPL-1.2
using Microsoft.Extensions.Logging;

namespace Aetheus.Front.Layout;

/// <summary>
/// PLAN-005 lot 9 / D48: says why a session ended, in the "session expired" toast, and reports the
/// same reason with a correlation id so the next spontaneous sign-out can be read in the system logs.
/// Once per ended session: without a pending reason (already announced) it does nothing. Kept out of
/// <c>MainLayout</c>, which calls it from its start-up and from its need-to-login handler.
/// </summary>
internal static class SessionEndAnnouncement
{
    public static void Announce(
        AuthStateProvider auth, NotifyHelper notify, IStringLocalizer<AppStrings> localizer, ApiClient api, ILogger logger)
    {
        if (auth.TakeSessionEndReason() is not { } reason)
            return;

        var correlationId = Guid.NewGuid().ToString("N");
        notify.Notify(OmniSeverity.Warning, "SessionExpired",
            $"{localizer["SessionExpiredDetail"].Value} {localizer[$"SessionEnd_{reason}"].Value} ({correlationId[..8]})");
        _ = ReportAsync(api, logger, reason, correlationId);
    }

    private static async Task ReportAsync(ApiClient api, ILogger logger, string reason, string correlationId)
    {
        try
        {
            await api.Auth.ReportSessionEndedAsync(new SessionEndedReport { Reason = reason, CorrelationId = correlationId });
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Could not report the session end ({Reason})", reason);
        }
    }
}
