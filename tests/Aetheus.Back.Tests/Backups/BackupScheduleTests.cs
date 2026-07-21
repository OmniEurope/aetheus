// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.AppBackups;

namespace Aetheus.Back.Tests.Backups;

public class BackupScheduleTests
{
    private static readonly DateTime Now = new(2026, 7, 9, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(2);

    [Fact]
    public void Due_WhenCronFiresInWindow_AndNoRecentRun()
        => Assert.True(BackupSchedule.IsDue("0 12 * * *", Now, lastRunUtc: null, Window));

    [Fact]
    public void NotDue_WhenAlreadyRanInsideWindow()
        => Assert.False(BackupSchedule.IsDue("0 12 * * *", Now, lastRunUtc: Now.AddMinutes(-1), Window));

    [Fact]
    public void Due_WhenLastRunOlderThanWindow()
        => Assert.True(BackupSchedule.IsDue("0 12 * * *", Now, lastRunUtc: Now.AddDays(-1), Window));

    [Fact]
    public void NotDue_WhenCronDoesNotFireNow()
        => Assert.False(BackupSchedule.IsDue("0 13 * * *", Now, lastRunUtc: null, Window));

    [Fact]
    public void NotDue_WhenCronNullOrBlank()
    {
        Assert.False(BackupSchedule.IsDue(null, Now, null, Window));
        Assert.False(BackupSchedule.IsDue("   ", Now, null, Window));
    }

    [Fact]
    public void NotDue_WhenCronInvalid()
        => Assert.False(BackupSchedule.IsDue("not a cron", Now, null, Window));
}
