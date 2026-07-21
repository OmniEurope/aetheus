// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Agent.Core.Configuration;
using Aetheus.Agent.Core.Executors;
using Aetheus.Back.Components.Apache;
using Aetheus.Back.Components.Docker;
using Aetheus.Back.Components.Mail;
using Aetheus.Back.Components.Rkhunter;
using Aetheus.Shared.Enums;
using Microsoft.Extensions.Options;

namespace Aetheus.Back.Tests.Architecture;

/// <summary>
/// Regression guard: every shell string produced by <c>*CommandHelper.Build*</c> methods that the
/// agent will route through <see cref="CommandValidator"/> must pass
/// <see cref="CommandValidator.IsAllowed"/> with the default allow-list. If a helper changes its
/// output shape (new binary, different quoting, extra flags), this test breaks BEFORE the command
/// silently fails in production.
///
/// Commands that are executed through elevated/sudo paths (mail setup, account management, DKIM,
/// portsentry setup) are validated separately as <b>expected rejections</b> - they contain shell
/// metacharacters (<c>$()</c>, <c>&gt;</c>, <c>;</c>) that the validator intentionally blocks.
/// Those commands reach the shell via the sudo-escalation pathway, not the standard executor.
/// If a refactor removes the metacharacters, the "rejected" test will break, signaling that the
/// command can (and should) be moved to the standard validator path.
/// </summary>
public class CommandHelperValidationTests
{
    private readonly CommandValidator _validator = new(Options.Create(new AetheusAgentOptions()));

    // ───────────────────────────────────────────────────────────────────
    // Mail - service commands that pass the default allow-list
    // ───────────────────────────────────────────────────────────────────

    public static TheoryData<string, string> MailServiceCommands_Allowed => new()
    {
        { "StartPostfix",    MailCommandHelper.BuildServiceCommand(MailAction.StartPostfix) },
        { "StopPostfix",     MailCommandHelper.BuildServiceCommand(MailAction.StopPostfix) },
        { "RestartPostfix",  MailCommandHelper.BuildServiceCommand(MailAction.RestartPostfix) },
        { "StartDovecot",    MailCommandHelper.BuildServiceCommand(MailAction.StartDovecot) },
        { "StopDovecot",     MailCommandHelper.BuildServiceCommand(MailAction.StopDovecot) },
        { "RestartDovecot",  MailCommandHelper.BuildServiceCommand(MailAction.RestartDovecot) },
    };

    [Theory]
    [MemberData(nameof(MailServiceCommands_Allowed))]
    public void MailServiceCommand_IsAllowed(string label, string command)
    {
        Assert.True(_validator.IsAllowed(command),
            $"Mail service command '{label}' should pass validation but was rejected: {command}");
    }

    // ───────────────────────────────────────────────────────────────────
    // Mail - service commands with actions not in the systemctl pattern
    //   reload/enable/disable are not in the default allow-list for systemctl
    // ───────────────────────────────────────────────────────────────────

    public static TheoryData<string, string> MailServiceCommands_NotInPattern => new()
    {
        { "ReloadPostfix",  MailCommandHelper.BuildServiceCommand(MailAction.ReloadPostfix) },
        { "ReloadDovecot",  MailCommandHelper.BuildServiceCommand(MailAction.ReloadDovecot) },
    };

    [Theory]
    [MemberData(nameof(MailServiceCommands_NotInPattern))]
    public void MailServiceCommand_ReloadNotInDefaultPattern(string label, string command)
    {
        // reload is not in the default systemctl pattern (start|stop|restart|status only)
        Assert.False(_validator.IsAllowed(command),
            $"Mail service command '{label}' should be rejected (reload not in default systemctl pattern): {command}");
    }

    // ───────────────────────────────────────────────────────────────────
    // Mail - commands with shell metacharacters (executed via sudo path)
    // ───────────────────────────────────────────────────────────────────

    public static TheoryData<string, string> MailCommands_WithMetachars => new()
    {
        { "FlushQueue",     MailCommandHelper.BuildServiceCommand(MailAction.FlushQueue) },
        { "ViewQueue",      MailCommandHelper.BuildServiceCommand(MailAction.ViewQueue) },
        { "TestConfig",     MailCommandHelper.BuildServiceCommand(MailAction.TestConfig) },
        { "GetLogs_postfix", MailCommandHelper.BuildGetLogsCommand("postfix", 100) },
        { "GetLogs_dovecot", MailCommandHelper.BuildGetLogsCommand("dovecot", 100) },
        // Mail full setup AND every incremental op (add/remove domain, add/delete account, add/remove alias,
        // dkim-rotate/read, change-password) no longer build shell commands - they dispatch typed
        // OperationKinds through the root-owned mail-setup / mail-manage helpers (S-FEAT-W8KN + the audit
        // "dead shell action" migration). See MailOperationExecutorTests for the argv coverage.
    };

    [Theory]
    [MemberData(nameof(MailCommands_WithMetachars))]
    public void MailCommand_WithMetachars_IsRejected(string label, string command)
    {
        Assert.False(_validator.IsAllowed(command),
            $"Mail command '{label}' contains shell metacharacters and should be rejected: {command}");
    }

    // ───────────────────────────────────────────────────────────────────
    // Mail - SpamAssassin service commands
    //   Only start/stop/restart match the default systemctl pattern
    // ───────────────────────────────────────────────────────────────────

    public static TheoryData<string, string> SpamAssassinCommands_Allowed => new()
    {
        { "start",   MailCommandHelper.BuildSpamAssassinServiceCommand("start") },
        { "stop",    MailCommandHelper.BuildSpamAssassinServiceCommand("stop") },
        { "restart", MailCommandHelper.BuildSpamAssassinServiceCommand("restart") },
        { "status",  MailCommandHelper.BuildSpamAssassinServiceCommand("status") },
    };

    [Theory]
    [MemberData(nameof(SpamAssassinCommands_Allowed))]
    public void SpamAssassinCommand_StartStopRestart_IsAllowed(string label, string command)
    {
        Assert.True(_validator.IsAllowed(command),
            $"SpamAssassin '{label}' should pass validation but was rejected: {command}");
    }

    public static TheoryData<string, string> SpamAssassinCommands_NotInPattern => new()
    {
        { "reload",  MailCommandHelper.BuildSpamAssassinServiceCommand("reload") },
        { "enable",  MailCommandHelper.BuildSpamAssassinServiceCommand("enable") },
        { "disable", MailCommandHelper.BuildSpamAssassinServiceCommand("disable") },
    };

    [Theory]
    [MemberData(nameof(SpamAssassinCommands_NotInPattern))]
    public void SpamAssassinCommand_ReloadEnableDisable_NotInDefaultPattern(string label, string command)
    {
        Assert.False(_validator.IsAllowed(command),
            $"SpamAssassin '{label}' should be rejected (not in default systemctl pattern): {command}");
    }

    // Portsentry no longer has any shell-string builders: it dispatches exclusively via typed
    // OperationKind ops (setup / unblock / service / logs / status), covered by
    // PortsentryOperationExecutorTests / PortsentryServiceTests. Nothing to validate here anymore.

    // ───────────────────────────────────────────────────────────────────
    // Rkhunter - setup + getlogs are designed metacharacter-free so they PASS the default
    // allow-list (apt-get/rkhunter/sed/export/echo patterns + tail). Locks the helper<->allow-list
    // lockstep the helper comments claim, so a format drift breaks the build instead of the agent.
    // ───────────────────────────────────────────────────────────────────

    public static TheoryData<string, string> RkhunterCommands_Allowed => new()
    {
        { "Setup_noEmail",   RkhunterCommandHelper.BuildSetupCommand(null) },
        { "Setup_withEmail", RkhunterCommandHelper.BuildSetupCommand("admin@example.com") },
        { "GetLogs",         RkhunterCommandHelper.BuildGetLogsCommand(100) },
    };

    [Theory]
    [MemberData(nameof(RkhunterCommands_Allowed))]
    public void RkhunterCommand_IsAllowed(string label, string command)
    {
        Assert.True(_validator.IsAllowed(command),
            $"Rkhunter command '{label}' should pass validation but was rejected: {command}");
    }

    // ───────────────────────────────────────────────────────────────────
    // Apache - every Build* helper string. Service/site/module/getlogs all carry shell
    // metacharacters (||, &&, 2>&1, 2>/dev/null) by design and reach the shell via the
    // elevated apache path, so the default validator must REJECT them (they must never
    // travel the standard unelevated executor). This closes the guard's Apache blind spot.
    // ───────────────────────────────────────────────────────────────────

    public static TheoryData<string, string> ApacheCommands_WithMetachars => new()
    {
        { "Start",          ApacheCommandHelper.BuildServiceCommand(ApacheAction.Start) },
        { "Stop",           ApacheCommandHelper.BuildServiceCommand(ApacheAction.Stop) },
        { "Restart",        ApacheCommandHelper.BuildServiceCommand(ApacheAction.Restart) },
        { "Reload",         ApacheCommandHelper.BuildServiceCommand(ApacheAction.Reload) },
        { "TestConfig",     ApacheCommandHelper.BuildServiceCommand(ApacheAction.TestConfig) },
        { "EnableSite",     ApacheCommandHelper.BuildEnableSiteCommand("mysite") },
        { "DisableSite",    ApacheCommandHelper.BuildDisableSiteCommand("mysite") },
        { "EnableModule",   ApacheCommandHelper.BuildEnableModuleCommand("proxy") },
        { "DisableModule",  ApacheCommandHelper.BuildDisableModuleCommand("proxy") },
        { "GetLogs_access", ApacheCommandHelper.BuildGetLogsCommand("access", 100) },
        { "GetLogs_error",  ApacheCommandHelper.BuildGetLogsCommand("error", 100) },
    };

    [Theory]
    [MemberData(nameof(ApacheCommands_WithMetachars))]
    public void ApacheCommand_WithMetachars_IsRejected(string label, string command)
    {
        Assert.False(_validator.IsAllowed(command),
            $"Apache command '{label}' contains shell metacharacters and must be rejected by the default validator: {command}");
    }

    // ───────────────────────────────────────────────────────────────────
    // Docker - EncodeBase64Shell wraps a base64 blob in `printf '...' | base64 -d`; the pipe
    // is a metacharacter, so the default validator rejects it (it is embedded into a larger
    // elevated docker shell, never run standalone through the standard executor).
    // ───────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("simple content")]
    [InlineData("multi\nline\ncontent with $pecial ; chars && metachars")]
    public void DockerEncodeBase64Shell_IsRejected(string content)
    {
        var command = DockerCommandHelper.EncodeBase64Shell(content);
        Assert.False(_validator.IsAllowed(command),
            $"DockerCommandHelper.EncodeBase64Shell output contains a pipe and must be rejected by the default validator: {command}");
    }

    // ───────────────────────────────────────────────────────────────────
    // Cross-cutting: command helpers must not produce empty/null strings
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public void AllBuildMethods_ProduceNonEmptyStrings()
    {
        var commands = new (string Label, string Command)[]
        {
            ("Mail.StartPostfix",        MailCommandHelper.BuildServiceCommand(MailAction.StartPostfix)),
            ("Mail.GetLogs",             MailCommandHelper.BuildGetLogsCommand("postfix", 50)),
            // Remove/delete/dkim-read builders were removed (typed ops via mail-manage) - covered by
            // MailOperationExecutorTests / MailServiceTests instead.
            ("Mail.SpamAssassin.start",  MailCommandHelper.BuildSpamAssassinServiceCommand("start")),
        };

        foreach (var (label, command) in commands)
        {
            Assert.False(string.IsNullOrWhiteSpace(command),
                $"{label} produced a null/empty command string");
        }
    }
}
