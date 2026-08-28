// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.Validation;

namespace Aetheus.Back.Components.Mail;

public static class MailCommandHelper
{
    // Field validation is shared with the agent via Aetheus.Shared.Validation.MailValidation
    // (single source of truth for the typed MailSetup operation); these thin wrappers keep the
    // backend call sites unchanged.
    public static bool IsValidDomainName(string name) => MailValidation.IsValidDomainName(name);

    public static bool IsValidEmail(string email) => MailValidation.IsValidEmail(email);

    public static bool IsValidDkimSelector(string selector) => MailValidation.IsValidDkimSelector(selector);

    public static bool IsValidPassword(string password) => MailValidation.IsValidPassword(password);

    /// <summary>
    /// Wraps an arbitrary string in single quotes for safe POSIX shell interpolation.
    /// Any embedded single quote is closed, escaped (<c>\'</c>), and reopened.
    /// Use whenever a value is included in a string passed to <c>bash -c</c>.
    /// </summary>
    public static string ShellQuote(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return "'" + value.Replace("'", "'\\''") + "'";
    }

    public static string BuildServiceCommand(MailAction action) => action switch
    {
        MailAction.StartPostfix => "systemctl start postfix",
        MailAction.StopPostfix => "systemctl stop postfix",
        MailAction.RestartPostfix => "systemctl restart postfix",
        MailAction.ReloadPostfix => "systemctl reload postfix",
        MailAction.StartDovecot => "systemctl start dovecot",
        MailAction.StopDovecot => "systemctl stop dovecot",
        MailAction.RestartDovecot => "systemctl restart dovecot",
        MailAction.ReloadDovecot => "systemctl reload dovecot",
        MailAction.FlushQueue => "postqueue -f",
        MailAction.ViewQueue => "postqueue -p",
        MailAction.TestConfig => "postfix check 2>&1 && echo 'Postfix config OK'",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
    };

    public static string BuildGetLogsCommand(string logType, int lines)
    {
        var unit = logType == "postfix" ? "postfix@-.service" : "dovecot";
        return $"journalctl -u {unit} --no-pager -n {lines} 2>/dev/null || tail -n {lines} /var/log/mail.log 2>/dev/null";
    }

    // S-FEAT-W8KN: add-domain / add-account / add-alias / dkim-rotate no longer build free-form shell
    // strings (they always failed for the non-root agent). They dispatch the typed MailAddDomain /
    // MailAddAccount / MailAddAlias / MailDkimRotate operations through the root-owned mail-manage
    // helper. Covered by MailServiceTests (dispatch) and MailOperationExecutorTests (argv + validation).

    // BuildRemoveDomainCommand / BuildDeleteAccountCommand / BuildRemoveAliasCommand /
    // BuildDkimKeyReadCommand were removed: those shell builders carried $(...), &&, sed and redirection
    // metacharacters the agent's CommandValidator rejects, so the tasks died silently while the DB row
    // was already gone (state drift). DeleteDomain / DeleteAccount / DeleteAlias / GetDnsRecords now
    // dispatch typed OperationKind.MailRemoveDomain / MailDeleteAccount / MailRemoveAlias / MailDkimRead
    // through the root-owned mail-manage helper (argv-exact, re-validated), like the add-* ops.
    // (SedEscape went with them - it was only used by those sed-based builders.)

    public static string BuildSpamAssassinServiceCommand(string action)
    {
        // Whitelist of permitted systemctl actions - prevents arbitrary action strings
        // (e.g. "restart spamassassin; rm -rf /") from reaching the shell.
        return action switch
        {
            "start" or "stop" or "restart" or "reload" or "enable" or "disable" or "status"
                => $"systemctl {action} spamassassin",
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
        };
    }

}
