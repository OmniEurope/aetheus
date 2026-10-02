// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Tests;

// Phase 3: the cron field validation shared by the backend (CronService) and the agent
// (CronOperationExecutor + the aetheus-cron-apply helper). Every charset deliberately excludes
// shell metacharacters so a validated field cannot carry an injection payload into the cron.d file.
public class CronValidationTests
{
    [Theory]
    [InlineData("root", true)]
    [InlineData("deploy_user", true)]
    [InlineData("_svc", true)]
    [InlineData("1bad", false)]
    [InlineData("has space", false)]
    [InlineData("with;semi", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValidUser(string? user, bool expected)
        => Assert.Equal(expected, CronValidation.IsValidUser(user));

    // #10: the cron-apply helper is reachable via an opt-in sudoers grant, so a root cron job is an
    // agent-compromise→root escalation path. Save rejects root (delete stays unrestricted).
    [Theory]
    [InlineData("root", false)]
    [InlineData("ROOT", false)]
    [InlineData("deploy_user", true)]
    [InlineData("_svc", true)]
    [InlineData("1bad", false)]
    [InlineData(null, false)]
    public void IsValidNonRootUser(string? user, bool expected)
        => Assert.Equal(expected, CronValidation.IsValidNonRootUser(user));

    [Theory]
    [InlineData("job-7", true)]
    [InlineData("abc_123", true)]
    [InlineData("has.dot", false)]   // dot → cron ignores the cron.d file
    [InlineData("../escape", false)] // path traversal
    [InlineData("bad id", false)]
    [InlineData("", false)]
    public void IsValidIdentifier(string id, bool expected)
        => Assert.Equal(expected, CronValidation.IsValidIdentifier(id));

    [Theory]
    [InlineData("*/5 * * * *", true)]
    [InlineData("0 2 * * 1", true)]
    [InlineData("*/30 * * * * *", false)] // 6-field (seconds): /etc/cron.d is strictly 5-field
    [InlineData("* * * *", false)]        // 4-field
    [InlineData("echo $(whoami)", false)]
    [InlineData("* * * * *; curl evil", false)]
    [InlineData("", false)]
    public void IsValidScheduleSyntax(string schedule, bool expected)
        => Assert.Equal(expected, CronValidation.IsValidScheduleSyntax(schedule));

    [Theory]
    [InlineData("/usr/bin/backup.sh", true)]
    [InlineData("/path/to/cmd -a --flag=1", true)]
    [InlineData("echo $(whoami)", false)]
    [InlineData("rm -rf /; curl evil.sh", false)]
    [InlineData("a | b", false)]
    [InlineData("echo `whoami`", false)]
    [InlineData("", false)]
    public void IsValidCommand(string command, bool expected)
        => Assert.Equal(expected, CronValidation.IsValidCommand(command));
}
