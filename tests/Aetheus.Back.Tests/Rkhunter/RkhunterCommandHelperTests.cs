// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Rkhunter;

namespace Aetheus.Back.Tests;

public class RkhunterCommandHelperTests
{
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
        Assert.Equal(expected, RkhunterCommandHelper.IsValidEmail(email));
    }

    // --- BuildActionCommand ---

    [Theory]
    [InlineData(RkhunterAction.UpdateDatabase, "rkhunter --update")]
    [InlineData(RkhunterAction.UpdateProperties, "rkhunter --propupd")]
    [InlineData(RkhunterAction.RunScan, "rkhunter --check")]
    public void BuildActionCommand_ReturnsExpectedCommand(RkhunterAction action, string expectedSubstring)
    {
        var cmd = RkhunterCommandHelper.BuildActionCommand(action);
        Assert.Contains(expectedSubstring, cmd);
        Assert.Contains("--nocolors", cmd);
    }

    // Guards against the silent-fail class that broke RKHunter scans in production: action
    // commands MUST be free of shell metacharacters (`&`, `|`, `>`, `<`, `;`, …) because the
    // agent's CommandValidator rejects them outright, and the rejection surfaces as a generic
    // exit -1 with no UI hint. If anyone re-adds `2>&1` or chains commands here, this fires.
    [Theory]
    [InlineData(RkhunterAction.UpdateDatabase)]
    [InlineData(RkhunterAction.UpdateProperties)]
    [InlineData(RkhunterAction.RunScan)]
    public void BuildActionCommand_HasNoShellMetacharacters(RkhunterAction action)
    {
        var cmd = RkhunterCommandHelper.BuildActionCommand(action);
        Assert.DoesNotMatch(@"[;|&`$(){}\<>\r\n\0]", cmd);
    }

    // --- BuildSetupCommand ---

    [Fact]
    public void BuildSetupCommand_NoMail_ReturnsBasicSetup()
    {
        var cmd = RkhunterCommandHelper.BuildSetupCommand(null);

        Assert.Contains("apt-get install", cmd);
        Assert.Contains("rkhunter", cmd);
        Assert.Contains("--update", cmd);
        Assert.Contains("--propupd", cmd);
        Assert.DoesNotContain("MAIL-ON-WARNING", cmd);
    }

    [Fact]
    public void BuildSetupCommand_EmptyMail_ReturnsBasicSetup()
    {
        var cmd = RkhunterCommandHelper.BuildSetupCommand("");

        Assert.DoesNotContain("MAIL-ON-WARNING", cmd);
    }

    [Fact]
    public void BuildSetupCommand_ValidMail_IncludesMailConfig()
    {
        var cmd = RkhunterCommandHelper.BuildSetupCommand("admin@example.com");

        Assert.Contains("MAIL-ON-WARNING", cmd);
        Assert.Contains("admin@example.com", cmd);
    }

    [Fact]
    public void BuildSetupCommand_InvalidMail_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            RkhunterCommandHelper.BuildSetupCommand("not-an-email"));
    }

    [Fact]
    public void BuildSetupCommand_InjectionAttempt_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            RkhunterCommandHelper.BuildSetupCommand("admin@example.com\"; rm -rf /; echo \""));
    }

    // Guards the silent-fail class: the agent's CommandValidator rejects any segment
    // containing a shell metacharacter, and only `&&` may join segments. If anyone
    // reintroduces `2>&1`, `||`, `;`, `2>/dev/null`, or `$( )` here, this fires.
    [Theory]
    [InlineData(null)]
    [InlineData("admin@example.com")]
    public void BuildSetupCommand_EverySegmentIsMetacharFree(string? mailOnWarning)
    {
        var cmd = RkhunterCommandHelper.BuildSetupCommand(mailOnWarning);
        foreach (var segment in cmd.Split("&&"))
            Assert.DoesNotMatch(@"[;|&`$(){}\<>\r\n\0]", segment);
    }

    // --- BuildGetLogsCommand ---

    [Fact]
    public void BuildGetLogsCommand_ReturnsCommandWithLines()
    {
        var cmd = RkhunterCommandHelper.BuildGetLogsCommand(200);
        Assert.Contains("tail -n 200", cmd);
        Assert.Contains("rkhunter.log", cmd);
    }

    [Fact]
    public void BuildGetLogsCommand_HasNoShellMetacharacters()
    {
        var cmd = RkhunterCommandHelper.BuildGetLogsCommand(200);
        Assert.DoesNotMatch(@"[;|&`$(){}\<>\r\n\0]", cmd);
    }
}
