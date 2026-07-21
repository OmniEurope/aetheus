// SPDX-License-Identifier: EUPL-1.2
using System.Text.RegularExpressions;
using Aetheus.Shared.Enums;

namespace Aetheus.Back.Components.Rkhunter;

public static partial class RkhunterCommandHelper
{
    // The agent's CommandValidator runs every task command through an allow-list and rejects
    // ALL shell metacharacters EXCEPT `&&`, which it accepts only between otherwise-valid
    // segments. So multi-step commands here must be `&&`-chains of single, metacharacter-free,
    // allow-listed invocations - no `;`, `|`, `||`, `$()`, backticks, or redirections (`2>&1`,
    // `2>/dev/null`). The agent captures stdout and stderr separately, so redirections are
    // unnecessary anyway; each whitelisted segment is pinned in AetheusAgentOptions.
    public static string BuildActionCommand(RkhunterAction action) => action switch
    {
        RkhunterAction.UpdateDatabase => "rkhunter --update --nocolors",
        RkhunterAction.UpdateProperties => "rkhunter --propupd --nocolors",
        RkhunterAction.RunScan => "rkhunter --check --skip-keypress --nocolors",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
    };

    public static bool IsValidEmail(string email) =>
        !string.IsNullOrWhiteSpace(email) && EmailRegex().IsMatch(email);

    public static string BuildSetupCommand(string? mailOnWarning)
    {
        // Each entry is one metacharacter-free, allow-listed command; string.Join glues them
        // with ` && ` into the single chain the agent's CommandValidator now accepts. The
        // chain is strict - a failed step aborts the rest (no `|| true`, which the validator
        // would reject) so a broken install surfaces instead of silently half-completing.
        var commands = new List<string>
        {
            "export DEBIAN_FRONTEND=noninteractive",
            "apt-get update -qq",
            "apt-get install -y -qq rkhunter",
            "rkhunter --update --nocolors",
            "rkhunter --propupd --nocolors"
        };

        if (!string.IsNullOrWhiteSpace(mailOnWarning))
        {
            if (!IsValidEmail(mailOnWarning))
                throw new ArgumentException("Mail on warning must be a valid email address.", nameof(mailOnWarning));

            commands.Add($"sed -i 's/^#\\?MAIL-ON-WARNING=.*/MAIL-ON-WARNING=\"{mailOnWarning}\"/' /etc/rkhunter.conf");
        }

        commands.Add("echo 'RKHunter setup completed successfully'");

        return string.Join(" && ", commands);
    }

    // A single metacharacter-free command. If the log is absent, `tail` exits non-zero and
    // writes its own "No such file" message to stderr (which the agent captures and surfaces)
    // - the old `2>/dev/null || echo …` fallback used metacharacters the validator rejects.
    public static string BuildGetLogsCommand(int lines) =>
        $"tail -n {lines} /var/log/rkhunter.log";

    [GeneratedRegex(@"^[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}$")]
    private static partial Regex EmailRegex();
}
