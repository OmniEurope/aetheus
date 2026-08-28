// SPDX-License-Identifier: EUPL-1.2
using System.Text.RegularExpressions;

namespace Aetheus.Shared.Validation;

/// <summary>
/// S-FEAT-W8KN: shared mail-field validation used on BOTH sides of the typed
/// <see cref="Aetheus.Shared.Enums.OperationKind.MailSetup"/> operation - the backend
/// (<c>MailService</c>) rejects bad input before queuing, and the agent's <c>MailOperationExecutor</c>
/// re-validates as a last-line defence before invoking the root-owned <c>mail-setup</c> sudo helper.
/// Every charset deliberately excludes shell metacharacters so a validated field can never carry an
/// injection payload to the helper. <c>MailCommandHelper</c> (backend) delegates to these so there is
/// a single source of truth for the patterns.
/// </summary>
public static partial class MailValidation
{
    // Hostname / domain: letters, digits, hyphens, dots - fully-qualified (at least one dot, a TLD ≥ 2).
    [GeneratedRegex(@"^[a-zA-Z0-9]([a-zA-Z0-9-]*[a-zA-Z0-9])?(\.[a-zA-Z0-9]([a-zA-Z0-9-]*[a-zA-Z0-9])?)*\.[a-zA-Z]{2,}$")]
    private static partial Regex DomainRegex();

    // Basic email pattern.
    [GeneratedRegex(@"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$")]
    private static partial Regex EmailRegex();

    // DKIM selector: alphanumeric, hyphens, underscores.
    [GeneratedRegex(@"^[a-zA-Z0-9_-]+$")]
    private static partial Regex DkimSelectorRegex();

    public static bool IsValidDomainName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && DomainRegex().IsMatch(name);

    public static bool IsValidEmail(string? email) =>
        !string.IsNullOrWhiteSpace(email) && EmailRegex().IsMatch(email);

    public static bool IsValidDkimSelector(string? selector) =>
        !string.IsNullOrWhiteSpace(selector) && DkimSelectorRegex().IsMatch(selector);

    public static bool IsValidPassword(string? password)
    {
        if (string.IsNullOrEmpty(password)
            || password.Length is < PasswordPolicy.MinimumLength or > PasswordPolicy.MaximumLength)
            return false;
        // Reject control chars / NUL - they would break the helper's stdin read and could smuggle
        // a newline into the dovecot users file.
        return !password.Any(char.IsControl);
    }

    // Mailbox quota in MB - a plain non-negative integer bounded well above any realistic mailbox.
    public static bool IsValidQuotaMb(int quotaMb) => quotaMb is >= 0 and <= 1_048_576;
}
