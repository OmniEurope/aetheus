// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.Mail;

public static class MailCommandHelper
{
    // Field validation is shared with the agent via Aetheus.Shared.Components.Shared.MailValidation
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

    // PLAN-005: BuildServiceCommand / BuildGetLogsCommand / BuildSpamAssassinServiceCommand were removed.
    // Their shell strings needed privileges the non-root agent never had; service control, queue, logs
    // and the rspamd spam filter now dispatch typed operations through the mail-manage helper.
}
