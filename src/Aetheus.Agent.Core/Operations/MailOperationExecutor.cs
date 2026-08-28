// SPDX-License-Identifier: EUPL-1.2
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Aetheus.Agent.Core.Operations;

/// <summary>
/// Typed Mail operations - service control (postfix/dovecot), queue management, config test,
/// and log reading. Each kind maps to a fixed argv; no shell interpolation, no user-controlled
/// input beyond the validated target string for log reading.
/// Service control goes through the controlled-sudo recipe (sudoers file deposited by the
/// install script). Queue and config commands run via sudo as well.
/// </summary>
public sealed class MailOperationExecutor(
    IOptions<AetheusAgentOptions> options,
    ILogger<MailOperationExecutor> logger) : IOperationExecutor
{
    private readonly AetheusAgentOptions _options = options.Value;

    // Root-owned helper deposited by install-agent-linux.sh (--module server-management /
    // --enable-mail-setup). The matching /etc/sudoers.d/aetheus-mail grants exactly this binary,
    // NOPASSWD. The helper re-validates every argument and is itself the security boundary.
    internal const string SetupHelperPath = "/usr/local/lib/aetheus/mail-setup";

    // S-FEAT-W8KN incremental ops. A second root-owned helper deposited by install-agent-linux.sh,
    // granted in the SAME /etc/sudoers.d/aetheus-mail file (its own Cmnd_Alias). The first argv token
    // is a fixed sub-command; the helper re-validates every following argument and is the boundary.
    internal const string ManageHelperPath = "/usr/local/lib/aetheus/mail-manage";

    public bool CanHandle(OperationKind kind) => kind is
        OperationKind.MailSetup or
        OperationKind.MailAddDomain or
        OperationKind.MailAddAccount or
        OperationKind.MailAddAlias or
        OperationKind.MailDkimRotate or
        OperationKind.MailChangePassword or
        OperationKind.MailRemoveDomain or
        OperationKind.MailDeleteAccount or
        OperationKind.MailRemoveAlias or
        OperationKind.MailDkimRead or
        OperationKind.MailStartPostfix or
        OperationKind.MailStopPostfix or
        OperationKind.MailRestartPostfix or
        OperationKind.MailReloadPostfix or
        OperationKind.MailStartDovecot or
        OperationKind.MailStopDovecot or
        OperationKind.MailRestartDovecot or
        OperationKind.MailReloadDovecot or
        OperationKind.MailFlushQueue or
        OperationKind.MailViewQueue or
        OperationKind.MailTestConfig or
        OperationKind.MailGetLogs;

    public async Task<ExecutorResult> ExecuteAsync(
        OperationKind kind,
        string target,
        int timeoutSeconds,
        Func<string, TaskLogLevel, Task> onOutput,
        CancellationToken cancellationToken)
    {
        var platformFailure = await OperationPlatformGuard.RequireLinuxAsync(
            onOutput, "Mail operations are only supported on Linux").ConfigureAwait(false);
        if (platformFailure is not null) return platformFailure;

        timeoutSeconds = Math.Clamp(timeoutSeconds, _options.MinTimeoutSeconds, _options.MaxTimeoutSeconds);

        if (kind == OperationKind.MailGetLogs)
            return await GetLogsAsync(target, timeoutSeconds, onOutput, cancellationToken).ConfigureAwait(false);

        return await OperationProcessRunner.RunOptionalAsync(
            BuildPsi(kind), timeoutSeconds, onOutput, logger, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ExecutorResult> ExecuteAsync(
        OperationKind kind,
        string target,
        IReadOnlyDictionary<string, string> envVars,
        int timeoutSeconds,
        Func<string, TaskLogLevel, Task> onOutput,
        CancellationToken cancellationToken)
    {
        // MailSetup and the incremental ops carry env vars (and sometimes a stdin secret); the rest
        // ignore them and fall through to the simpler overload.
        return kind switch
        {
            OperationKind.MailSetup =>
                await SetupAsync(target, envVars, timeoutSeconds, onOutput, cancellationToken).ConfigureAwait(false),
            OperationKind.MailAddDomain or
            OperationKind.MailAddAccount or
            OperationKind.MailAddAlias or
            OperationKind.MailDkimRotate or
            OperationKind.MailChangePassword or
            OperationKind.MailRemoveDomain or
            OperationKind.MailDeleteAccount or
            OperationKind.MailRemoveAlias or
            OperationKind.MailDkimRead =>
                await ManageAsync(kind, target, envVars, timeoutSeconds, onOutput, cancellationToken).ConfigureAwait(false),
            _ =>
                await ExecuteAsync(kind, target, timeoutSeconds, onOutput, cancellationToken).ConfigureAwait(false)
        };
    }

    /// <summary>
    /// S-FEAT-W8KN: full mail-stack setup via the root-owned <c>mail-setup</c> helper. The domain is
    /// the target; hostname/selector/email/quota arrive in <c>AETHEUS_MAIL_*</c> env vars and the
    /// admin password over stdin. Every field is re-validated here (last-line defence) and passed
    /// argv-only to the helper - no shell, no interpolation. The helper re-validates too and is the
    /// security boundary.
    /// </summary>
    private async Task<ExecutorResult> SetupAsync(
        string domain,
        IReadOnlyDictionary<string, string> envVars,
        int timeoutSeconds,
        Func<string, TaskLogLevel, Task> onOutput,
        CancellationToken cancellationToken)
    {
        envVars.TryGetValue(MailSetupEnv.Hostname, out var hostname);
        envVars.TryGetValue(MailSetupEnv.DkimSelector, out var selector);
        envVars.TryGetValue(MailSetupEnv.AdminEmail, out var email);
        envVars.TryGetValue(MailSetupEnv.QuotaMb, out var quotaRaw);
        envVars.TryGetValue(MailSetupEnv.AdminPassword, out var password);

        // Validate before the OS check so a malformed request is rejected without spawning anything
        // on any platform (mirrors PackageOperationExecutor).
        if (!MailValidation.IsValidDomainName(domain) ||
            !MailValidation.IsValidDomainName(hostname) ||
            !MailValidation.IsValidDkimSelector(selector) ||
            !MailValidation.IsValidEmail(email) ||
            !int.TryParse(quotaRaw, NumberStyles.None, CultureInfo.InvariantCulture, out var quota) ||
            !MailValidation.IsValidQuotaMb(quota) ||
            !MailValidation.IsValidPassword(password))
        {
            logger.LogWarning("Rejected mail setup with invalid parameters");
            await onOutput("Invalid mail setup parameters", TaskLogLevel.Error).ConfigureAwait(false);
            return new ExecutorResult(-1, false);
        }

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            await onOutput("Mail setup is only supported on Linux", TaskLogLevel.Error).ConfigureAwait(false);
            return new ExecutorResult(-1, false);
        }

        timeoutSeconds = Math.Clamp(timeoutSeconds, _options.MinTimeoutSeconds, _options.MaxTimeoutSeconds);

        var psi = SudoProcessStartInfo.Create();
        foreach (var arg in BuildSetupArgv(hostname!, domain, selector!, email!, quota))
            psi.ArgumentList.Add(arg);

        // Password over stdin (newline-terminated for the helper's `read`); never on the argv, so it
        // stays off the process list.
        return await ProcessRunner.RunAsync(
            psi, timeoutSeconds, onOutput, logger, cancellationToken, standardInput: password + "\n").ConfigureAwait(false);
    }

    // Full argv for `sudo` (unit-testable without spawning a process): `-n`, the helper path, then the
    // validated positional params in the exact order the helper expects. The password is NOT here.
    internal static IReadOnlyList<string> BuildSetupArgv(
        string hostname, string domain, string selector, string email, int quotaMb) =>
        ["-n", SetupHelperPath, hostname, domain, selector, email, quotaMb.ToString(CultureInfo.InvariantCulture)];

    /// <summary>
    /// S-FEAT-W8KN incremental ops: add-domain / add-account / add-alias / dkim-rotate via the root-owned
    /// <c>mail-manage</c> helper. Every field is re-validated here (last-line defence) and passed argv-only
    /// after the fixed sub-command token - no shell, no interpolation. The account password (add-account
    /// only) rides over stdin, never argv. The helper re-validates too and is the security boundary.
    /// </summary>
    private async Task<ExecutorResult> ManageAsync(
        OperationKind kind,
        string target,
        IReadOnlyDictionary<string, string> envVars,
        int timeoutSeconds,
        Func<string, TaskLogLevel, Task> onOutput,
        CancellationToken cancellationToken)
    {
        var (argv, stdin) = BuildManageCommand(kind, target, envVars);

        if (argv is null)
        {
            logger.LogWarning("Rejected mail {Kind} with invalid parameters", kind);
            await onOutput("Invalid mail operation parameters", TaskLogLevel.Error).ConfigureAwait(false);
            return new ExecutorResult(-1, false);
        }

        var platformFailure = await OperationPlatformGuard.RequireLinuxAsync(
            onOutput, "Mail operations are only supported on Linux").ConfigureAwait(false);
        if (platformFailure is not null) return platformFailure;

        timeoutSeconds = Math.Clamp(timeoutSeconds, _options.MinTimeoutSeconds, _options.MaxTimeoutSeconds);

        var psi = SudoProcessStartInfo.Create();
        foreach (var arg in argv)
            psi.ArgumentList.Add(arg);

        return await ProcessRunner.RunAsync(
            psi, timeoutSeconds, onOutput, logger, cancellationToken, standardInput: stdin).ConfigureAwait(false);
    }

    private static (IReadOnlyList<string>? Argv, string? Stdin) BuildManageCommand(
        OperationKind kind,
        string target,
        IReadOnlyDictionary<string, string> envVars) =>
        kind switch
        {
            OperationKind.MailAddDomain => DomainCommand("add-domain", target),
            OperationKind.MailAddAccount => AddAccountCommand(target, envVars),
            OperationKind.MailAddAlias => AddAliasCommand(target, envVars),
            OperationKind.MailDkimRotate => RotateDkimCommand(target, envVars),
            OperationKind.MailChangePassword => ChangePasswordCommand(target, envVars),
            OperationKind.MailRemoveDomain => DomainCommand("remove-domain", target),
            OperationKind.MailDeleteAccount => DeleteAccountCommand(target, envVars),
            OperationKind.MailRemoveAlias => EmailCommand("remove-alias", target),
            OperationKind.MailDkimRead => DkimReadCommand(target),
            _ => (null, null)
        };

    private static (IReadOnlyList<string>?, string?) DomainCommand(string command, string target) =>
        MailValidation.IsValidDomainName(target)
            ? (["-n", ManageHelperPath, command, target], null)
            : (null, null);

    private static (IReadOnlyList<string>?, string?) EmailCommand(string command, string target) =>
        MailValidation.IsValidEmail(target)
            ? (["-n", ManageHelperPath, command, target], null)
            : (null, null);

    private static (IReadOnlyList<string>?, string?) AddAccountCommand(
        string target,
        IReadOnlyDictionary<string, string> envVars)
    {
        envVars.TryGetValue(MailSetupEnv.Domain, out var domain);
        envVars.TryGetValue(MailSetupEnv.QuotaMb, out var quotaRaw);
        envVars.TryGetValue(MailSetupEnv.AccountPassword, out var password);
        if (!MailValidation.IsValidEmail(target)
            || !MailValidation.IsValidDomainName(domain)
            || !int.TryParse(quotaRaw, NumberStyles.None, CultureInfo.InvariantCulture, out var quota)
            || !MailValidation.IsValidQuotaMb(quota)
            || !MailValidation.IsValidPassword(password))
            return (null, null);
        return (
            ["-n", ManageHelperPath, "add-account", target, domain!, quota.ToString(CultureInfo.InvariantCulture)],
            password + "\n");
    }

    private static (IReadOnlyList<string>?, string?) AddAliasCommand(
        string target,
        IReadOnlyDictionary<string, string> envVars)
    {
        envVars.TryGetValue(MailSetupEnv.AliasDestination, out var destination);
        return MailValidation.IsValidEmail(target) && MailValidation.IsValidEmail(destination)
            ? (["-n", ManageHelperPath, "add-alias", target, destination!], null)
            : (null, null);
    }

    private static (IReadOnlyList<string>?, string?) RotateDkimCommand(
        string target,
        IReadOnlyDictionary<string, string> envVars)
    {
        envVars.TryGetValue(MailSetupEnv.NewSelector, out var selector);
        return MailValidation.IsValidDomainName(target) && MailValidation.IsValidDkimSelector(selector)
            ? (["-n", ManageHelperPath, "dkim-rotate", target, selector!], null)
            : (null, null);
    }

    private static (IReadOnlyList<string>?, string?) ChangePasswordCommand(
        string target,
        IReadOnlyDictionary<string, string> envVars)
    {
        envVars.TryGetValue(MailSetupEnv.AccountPassword, out var password);
        return MailValidation.IsValidEmail(target) && MailValidation.IsValidPassword(password)
            ? (["-n", ManageHelperPath, "change-password", target], password + "\n")
            : (null, null);
    }

    private static (IReadOnlyList<string>?, string?) DeleteAccountCommand(
        string target,
        IReadOnlyDictionary<string, string> envVars)
    {
        envVars.TryGetValue(MailSetupEnv.Domain, out var domain);
        return MailValidation.IsValidEmail(target) && MailValidation.IsValidDomainName(domain)
            ? (["-n", ManageHelperPath, "delete-account", target, domain!], null)
            : (null, null);
    }

    private static (IReadOnlyList<string>?, string?) DkimReadCommand(string target) =>
        MailValidation.IsValidDkimSelector(target)
            ? (["-n", ManageHelperPath, "dkim-read", target], null)
            : (null, null);

    /// <summary>
    /// Maps a Mail operation kind to a fully-resolved argv. Service control operations go
    /// through sudo; queue and config commands also need sudo for postfix/postqueue access.
    /// </summary>
    private static ProcessStartInfo? BuildPsi(OperationKind kind)
    {
        var psi = SudoProcessStartInfo.Create();
        psi.ArgumentList.Add("-n"); // never prompt - NOPASSWD required by sudoers rule

        switch (kind)
        {
            case OperationKind.MailStartPostfix:
                psi.ArgumentList.Add("/bin/systemctl");
                psi.ArgumentList.Add("start");
                psi.ArgumentList.Add("postfix.service");
                return psi;
            case OperationKind.MailStopPostfix:
                psi.ArgumentList.Add("/bin/systemctl");
                psi.ArgumentList.Add("stop");
                psi.ArgumentList.Add("postfix.service");
                return psi;
            case OperationKind.MailRestartPostfix:
                psi.ArgumentList.Add("/bin/systemctl");
                psi.ArgumentList.Add("restart");
                psi.ArgumentList.Add("postfix.service");
                return psi;
            case OperationKind.MailReloadPostfix:
                psi.ArgumentList.Add("/bin/systemctl");
                psi.ArgumentList.Add("reload");
                psi.ArgumentList.Add("postfix.service");
                return psi;
            case OperationKind.MailStartDovecot:
                psi.ArgumentList.Add("/bin/systemctl");
                psi.ArgumentList.Add("start");
                psi.ArgumentList.Add("dovecot.service");
                return psi;
            case OperationKind.MailStopDovecot:
                psi.ArgumentList.Add("/bin/systemctl");
                psi.ArgumentList.Add("stop");
                psi.ArgumentList.Add("dovecot.service");
                return psi;
            case OperationKind.MailRestartDovecot:
                psi.ArgumentList.Add("/bin/systemctl");
                psi.ArgumentList.Add("restart");
                psi.ArgumentList.Add("dovecot.service");
                return psi;
            case OperationKind.MailReloadDovecot:
                psi.ArgumentList.Add("/bin/systemctl");
                psi.ArgumentList.Add("reload");
                psi.ArgumentList.Add("dovecot.service");
                return psi;
            case OperationKind.MailFlushQueue:
                psi.ArgumentList.Add("/usr/sbin/postqueue");
                psi.ArgumentList.Add("-f");
                return psi;
            case OperationKind.MailViewQueue:
                psi.ArgumentList.Add("/usr/sbin/postqueue");
                psi.ArgumentList.Add("-p");
                return psi;
            case OperationKind.MailTestConfig:
                psi.ArgumentList.Add("/usr/sbin/postfix");
                psi.ArgumentList.Add("check");
                return psi;
            default:
                return null;
        }
    }

    /// <summary>
    /// Reads mail logs via <c>journalctl</c>. Target is the log source (already validated by
    /// <c>OperationTargetValidator</c> to be "postfix" or "dovecot").
    /// No sudo needed for journalctl log reading.
    /// </summary>
    private async Task<ExecutorResult> GetLogsAsync(
        string target,
        int timeoutSeconds,
        Func<string, TaskLogLevel, Task> onOutput,
        CancellationToken cancellationToken)
    {
        // Map target to the systemd unit name
        var unit = target switch
        {
            "postfix" => "postfix@-.service",
            "dovecot" => "dovecot.service",
            _ => "postfix@-.service"
        };

        var psi = new ProcessStartInfo
        {
            FileName = "journalctl",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("-u");
        psi.ArgumentList.Add(unit);
        psi.ArgumentList.Add("-n");
        psi.ArgumentList.Add("100");
        psi.ArgumentList.Add("--no-pager");

        return await ProcessRunner.RunAsync(psi, timeoutSeconds, onOutput, logger, cancellationToken).ConfigureAwait(false);
    }
}
