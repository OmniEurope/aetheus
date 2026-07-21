// SPDX-License-Identifier: EUPL-1.2
using Cronos;

namespace Aetheus.Back.Components.AppBackups;

/// <summary>
/// Pure due-calc for the backup scheduler (PLAN-006 4.3): a cron item is due iff its next occurrence
/// falls inside the current tick window AND it has not already fired within that window (last-run guard).
/// Extracted so the scheduling decision is unit-testable without a hosted service.
/// </summary>
public static class BackupSchedule
{
    public static bool IsDue(string? cron, DateTime nowUtc, DateTime? lastRunUtc, TimeSpan window)
    {
        if (string.IsNullOrWhiteSpace(cron)) return false;

        CronExpression expr;
        try { expr = CronExpression.Parse(cron); }
        catch (CronFormatException) { return false; }

        var next = expr.GetNextOccurrence(nowUtc - window, inclusive: true);
        if (next is null || next.Value > nowUtc || next.Value <= nowUtc - window)
            return false;

        // De-dupe: don't fire twice inside the same window if we already ran/checked recently.
        if (lastRunUtc.HasValue && (nowUtc - lastRunUtc.Value) < window)
            return false;

        return true;
    }
}
