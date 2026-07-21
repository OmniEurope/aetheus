// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Shared.Constants;

/// <summary>
/// S-FEAT-W8KN: env-var keys carrying the <see cref="Aetheus.Shared.Enums.OperationKind.MailSetup"/>
/// parameters from the backend to the agent. The mail domain travels in the task target; everything
/// else rides in these keys. The admin password is the one secret - it is delivered to the helper via
/// stdin, never argv, so it stays off the process list. Shared so both sides agree on the names.
/// </summary>
public static class MailSetupEnv
{
    public const string Hostname = "AETHEUS_MAIL_HOSTNAME";
    public const string DkimSelector = "AETHEUS_MAIL_DKIM_SELECTOR";
    public const string AdminEmail = "AETHEUS_MAIL_ADMIN_EMAIL";
    public const string QuotaMb = "AETHEUS_MAIL_QUOTA_MB";
    public const string AdminPassword = "AETHEUS_MAIL_ADMIN_PASSWORD";

    // --- S-FEAT-W8KN incremental ops (mail-manage helper) ---
    // The parent domain for an add-account operation (the account email is the task target).
    public const string Domain = "AETHEUS_MAIL_DOMAIN";

    // The mailbox password for an add-account operation. Like AdminPassword it is piped to the helper
    // over stdin (never argv) and encrypted at rest in the task's EnvironmentVariables.
    public const string AccountPassword = "AETHEUS_MAIL_ACCOUNT_PASSWORD";

    // The destination address for an add-alias operation (the source email is the task target).
    public const string AliasDestination = "AETHEUS_MAIL_ALIAS_DESTINATION";

    // The new DKIM selector for a dkim-rotate operation (the domain is the task target).
    public const string NewSelector = "AETHEUS_MAIL_NEW_SELECTOR";
}
