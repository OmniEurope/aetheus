// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Mail;
using Aetheus.Shared.Enums;

namespace Aetheus.Back.Tests;

public class MailCommandHelperTests
{
    // --- IsValidDomainName ---

    [Theory]
    [InlineData("example.com", true)]
    [InlineData("sub.example.com", true)]
    [InlineData("my-domain.co.uk", true)]
    [InlineData("a.io", true)]
    [InlineData("test123.example.org", true)]
    [InlineData("", false)]
    [InlineData(" ", false)]
    [InlineData("noextension", false)]
    [InlineData("-invalid.com", false)]
    [InlineData("invalid-.com", false)]
    [InlineData(".com", false)]
    [InlineData("ex ample.com", false)]
    [InlineData("example..com", false)]
    [InlineData("example.c", false)]
    public void IsValidDomainName_ReturnsExpected(string name, bool expected)
    {
        Assert.Equal(expected, MailCommandHelper.IsValidDomainName(name));
    }

    // --- IsValidEmail ---

    [Theory]
    [InlineData("user@example.com", true)]
    [InlineData("admin@sub.domain.org", true)]
    [InlineData("first.last@company.co", true)]
    [InlineData("user+tag@example.com", true)]
    [InlineData("u@x.io", true)]
    [InlineData("", false)]
    [InlineData(" ", false)]
    [InlineData("noatsign", false)]
    [InlineData("@domain.com", false)]
    [InlineData("user@", false)]
    [InlineData("user@.com", false)]
    [InlineData("user@domain", false)]
    [InlineData("user @domain.com", false)]
    public void IsValidEmail_ReturnsExpected(string email, bool expected)
    {
        Assert.Equal(expected, MailCommandHelper.IsValidEmail(email));
    }

    // --- IsValidDkimSelector ---

    [Theory]
    [InlineData("default", true)]
    [InlineData("mail", true)]
    [InlineData("dkim2024", true)]
    [InlineData("my-selector", true)]
    [InlineData("my_selector", true)]
    [InlineData("a", true)]
    [InlineData("", false)]
    [InlineData(" ", false)]
    [InlineData("bad selector", false)]
    [InlineData("bad.selector", false)]
    [InlineData("bad@selector", false)]
    [InlineData("bad/selector", false)]
    public void IsValidDkimSelector_ReturnsExpected(string selector, bool expected)
    {
        Assert.Equal(expected, MailCommandHelper.IsValidDkimSelector(selector));
    }

    // --- BuildServiceCommand ---

    [Theory]
    [InlineData(MailAction.StartPostfix, "systemctl start postfix")]
    [InlineData(MailAction.StopPostfix, "systemctl stop postfix")]
    [InlineData(MailAction.RestartPostfix, "systemctl restart postfix")]
    [InlineData(MailAction.ReloadPostfix, "systemctl reload postfix")]
    [InlineData(MailAction.StartDovecot, "systemctl start dovecot")]
    [InlineData(MailAction.StopDovecot, "systemctl stop dovecot")]
    [InlineData(MailAction.RestartDovecot, "systemctl restart dovecot")]
    [InlineData(MailAction.ReloadDovecot, "systemctl reload dovecot")]
    [InlineData(MailAction.FlushQueue, "postqueue -f")]
    [InlineData(MailAction.ViewQueue, "postqueue -p")]
    [InlineData(MailAction.TestConfig, "postfix check 2>&1 && echo 'Postfix config OK'")]
    public void BuildServiceCommand_ReturnsExpected(MailAction action, string expected)
    {
        Assert.Equal(expected, MailCommandHelper.BuildServiceCommand(action));
    }

    [Fact]
    public void BuildServiceCommand_InvalidAction_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MailCommandHelper.BuildServiceCommand((MailAction)999));
    }

    // --- BuildGetLogsCommand ---

    [Fact]
    public void BuildGetLogsCommand_Postfix_UsesPostfixUnit()
    {
        var result = MailCommandHelper.BuildGetLogsCommand("postfix", 100);

        Assert.Contains("postfix@-.service", result);
        Assert.Contains("-n 100", result);
    }

    [Fact]
    public void BuildGetLogsCommand_Dovecot_UsesDovecotUnit()
    {
        var result = MailCommandHelper.BuildGetLogsCommand("dovecot", 50);

        Assert.Contains("-u dovecot", result);
        Assert.Contains("-n 50", result);
    }

    // Mail full setup no longer builds a shell command (BuildSetupCommand was removed): it dispatches
    // the typed OperationKind.MailSetup through the root-owned mail-setup helper so the non-root agent
    // can run it via the controlled-sudo recipe (S-FEAT-W8KN). The setup path is covered by
    // MailServiceTests (dispatch) and MailOperationExecutorTests (argv + validation).

    // S-FEAT-W8KN: BuildAddDomainCommand / BuildAddAccountCommand / BuildAddAliasCommand /
    // BuildDkimRotateCommand were removed - those ops now dispatch the typed MailAddDomain /
    // MailAddAccount / MailAddAlias / MailDkimRotate operations through the root-owned mail-manage
    // helper. Covered by MailServiceTests (dispatch) and MailOperationExecutorTests (argv + validation).

    // BuildRemoveDomainCommand / BuildDeleteAccountCommand / BuildRemoveAliasCommand /
    // BuildDkimKeyReadCommand were removed (audit "dead shell action" migration): those sed/$()-based
    // shell builders were rejected by the agent CommandValidator, so the tasks died silently while the DB
    // row was already gone. DeleteDomain / DeleteAccount / DeleteAlias / GetDnsRecords now dispatch the
    // typed MailRemoveDomain / MailDeleteAccount / MailRemoveAlias / MailDkimRead operations through the
    // root-owned mail-manage helper. Covered by MailServiceTests (dispatch) and MailOperationExecutorTests
    // (argv + validation). S-TECH-MCPW change-password was migrated the same way earlier.
}
