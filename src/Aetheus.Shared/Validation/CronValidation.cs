// SPDX-License-Identifier: EUPL-1.2
using System.Text.RegularExpressions;

namespace Aetheus.Shared.Validation;

/// <summary>
/// Phase 3: shared cron-field validation used on BOTH sides of the typed CronSave/CronDelete
/// operation - the backend (<c>CronService</c>) rejects bad input before queuing, and the agent's
/// <c>CronOperationExecutor</c> re-validates as a last-line defence before invoking the
/// <c>aetheus-cron-apply</c> sudo helper. Regex-only (no Cronos dependency) so it lives in the
/// shared assembly; the backend layers a full Cronos parse on top of the schedule syntax check.
/// Every charset deliberately excludes shell metacharacters so a validated field can never carry
/// an injection payload into the cron.d file the helper writes.
/// </summary>
public static partial class CronValidation
{
    // Unix username: starts with a letter/underscore, then letters/digits/underscore/dash, max 32.
    [GeneratedRegex(@"^[a-z_][a-z0-9_-]{0,31}$", RegexOptions.IgnoreCase)]
    public static partial Regex UserRegex();

    // Aetheus-assigned job id - also the cron.d filename suffix (aetheus-<id>), so NO dot
    // (run-parts/cron ignore dotted files) and no slash (path traversal).
    [GeneratedRegex(@"^[a-zA-Z0-9_-]{1,64}$")]
    public static partial Regex IdRegex();

    // Cron schedule: digits, stars, slashes, commas, dashes, spaces only (no shell metachars).
    [GeneratedRegex(@"^[0-9*/,\-\s]+$")]
    public static partial Regex ScheduleRegex();

    // Command: alnum + common safe chars. Blocks shell metachars: `, $, (, ), |, ;, &, >, <, \n, \r, ", '.
    [GeneratedRegex(@"^[A-Za-z0-9 _/.:@=,\-]+$")]
    public static partial Regex CommandRegex();

    public static bool IsValidUser(string? user) => !string.IsNullOrEmpty(user) && UserRegex().IsMatch(user);

    /// <summary>Cron jobs may not run as <c>root</c>. The <c>aetheus-cron-apply</c> helper is reachable
    /// through an opt-in (<c>--module server-management</c>) NOPASSWD sudoers grant, so allowing a root job
    /// would turn agent compromise into a root-cron escalation path. Enforced backend-side
    /// (<c>CronService.SaveJobAsync</c>) and agent-side (<c>CronOperationExecutor</c>); the root-owned helper
    /// re-checks as a last line. Deletion is intentionally NOT restricted, so a pre-existing root entry can
    /// still be removed.</summary>
    public static bool IsValidNonRootUser(string? user) =>
        IsValidUser(user) && !string.Equals(user, "root", StringComparison.OrdinalIgnoreCase);

    public static bool IsValidIdentifier(string? id) => !string.IsNullOrEmpty(id) && IdRegex().IsMatch(id);

    /// <summary>Regex-only schedule syntax check (charset + length + field count). A <c>/etc/cron.d</c>
    /// entry is strictly 5-field (minute hour dom month dow); the file format reads a 6th field as the
    /// run-as user, so a 6-field (seconds) schedule would silently corrupt the entry. We therefore reject
    /// anything that is not exactly 5 fields here, on both the backend and the agent. The backend
    /// additionally parses with Cronos to reject syntactically-valid-but-semantically-broken expressions.</summary>
    public static bool IsValidScheduleSyntax(string? schedule)
    {
        if (string.IsNullOrWhiteSpace(schedule) || schedule.Length > 100 || !ScheduleRegex().IsMatch(schedule))
            return false;
        return schedule.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length == 5;
    }

    public static bool IsValidCommand(string? command) =>
        !string.IsNullOrWhiteSpace(command) && command.Length <= 1000 && CommandRegex().IsMatch(command);
}
