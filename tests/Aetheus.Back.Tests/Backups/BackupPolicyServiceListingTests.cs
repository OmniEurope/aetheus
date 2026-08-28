// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.AppBackups;
using Aetheus.Back.Components.Audit;
using Aetheus.Back.Components.Servers;
using Aetheus.Back.Components.Tasks;
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Exceptions;
using Aetheus.Back.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Aetheus.Back.Tests.Backups;

/// <summary>
/// The listing, mutation and restore-check paths of the backup policy service that
/// <see cref="BackupPolicyServiceTests"/> does not reach: the empty-scope short circuit, the
/// update/delete lifecycle with its "keep the stored password" rule, the run listing, the
/// online-server precondition on run-now, and the restore-check dispatch.
/// </summary>
public sealed class BackupPolicyServiceListingTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTime NowUtc = Now.UtcDateTime;

    private readonly IBackupRepository _repo = Substitute.For<IBackupRepository>();
    private readonly IServerRepository _serverRepo = Substitute.For<IServerRepository>();
    private readonly ITaskService _taskService = Substitute.For<ITaskService>();
    private readonly IEncryptionService _encryption = Substitute.For<IEncryptionService>();
    private readonly IAuditService _audit = Substitute.For<IAuditService>();
    private readonly IEntityChangeNotifier _notifier = Substitute.For<IEntityChangeNotifier>();
    private readonly FakeTimeProvider _clock = new(Now);
    private readonly BackupPolicyService _service;

    public BackupPolicyServiceListingTests()
    {
        _encryption.EncryptValue(Arg.Any<string>()).Returns(call => "enc:" + call.Arg<string>());
        _encryption.DecryptValue(Arg.Any<string>()).Returns(call => call.Arg<string>()["enc:".Length..]);
        _repo.GetProjectOrganizationIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(4);
        _service = new BackupPolicyService(
            _repo, _serverRepo, _taskService, _encryption, _clock, _audit, _notifier);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static BackupPolicy Policy(
        int id = 1, string name = "nightly", string? encryptedPassword = "enc:secret",
        string? filePathsJson = "[\"/srv/app\"]") =>
        new()
        {
            Id = id,
            Name = name,
            Enabled = true,
            ProjectId = 10,
            ServerId = 20,
            DbEngine = BackupDbEngine.Postgres,
            DbHost = "127.0.0.1",
            DbPort = 5432,
            DbName = "app",
            DbUser = "app",
            DbPasswordEncrypted = encryptedPassword,
            FilePathsJson = filePathsJson,
            ScheduleCron = "0 3 * * *",
            RetentionCount = 7
        };

    private static UpdateBackupPolicyRequest UpdateRequest(
        string? password = null, string cron = "0 4 * * *", List<string>? filePaths = null) =>
        new()
        {
            Name = "  renamed  ",
            Enabled = false,
            DbEngine = BackupDbEngine.MySql,
            DbHost = "db.internal",
            DbPort = 3306,
            DbName = "renamed-db",
            DbUser = "renamed-user",
            DbPassword = password,
            FilePaths = filePaths ?? [],
            ScheduleCron = cron,
            RetentionCount = 30,
            RestoreCheckCron = "0 5 * * 0"
        };

    // ---------- listing ----------

    [Fact]
    public async Task GetPoliciesAsync_ShortCircuitsWhenTheCallerCanSeeNoProjectAtAll()
    {
        var page = await _service.GetPoliciesAsync([], new PaginationRequest { Page = 2, PageSize = 10 }, Ct);

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
        Assert.Equal(2, page.Page);
        Assert.Equal(10, page.PageSize);
        await _repo.DidNotReceive().GetPoliciesPagedAsync(
            Arg.Any<IReadOnlyCollection<int>?>(), Arg.Any<string?>(), Arg.Any<string?>(),
            Arg.Any<bool>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetPoliciesAsync_ForwardsTheNormalizedPageAndMapsTheRows()
    {
        _repo.GetPoliciesPagedAsync(
                Arg.Any<IReadOnlyCollection<int>?>(), "needle", "Name", true, 1, 25, Arg.Any<CancellationToken>())
            .Returns(([Policy()], 1));

        var page = await _service.GetPoliciesAsync(
            null,
            new PaginationRequest { Page = 1, PageSize = 25, Search = "needle", SortBy = "Name", SortDescending = true },
            Ct);

        var policy = Assert.Single(page.Items);
        Assert.Equal("nightly", policy.Name);
        Assert.True(policy.HasPassword);
        Assert.Equal(["/srv/app"], policy.FilePaths);
        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task GetPoliciesAsync_MapsAPolicyWithNeitherPasswordNorFilePaths()
    {
        _repo.GetPoliciesPagedAsync(
                Arg.Any<IReadOnlyCollection<int>?>(), Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<bool>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(([Policy(encryptedPassword: null, filePathsJson: null)], 1));

        var policy = Assert.Single((await _service.GetPoliciesAsync(null, new PaginationRequest(), Ct)).Items);

        Assert.False(policy.HasPassword);
        Assert.Empty(policy.FilePaths);
    }

    [Fact]
    public async Task GetRunsAsync_ForwardsTheRequestAndMapsEachRun()
    {
        _repo.GetRunsForPolicyPagedAsync(5, "needle", "StartedAt", true, 1, 25, Arg.Any<CancellationToken>())
            .Returns((
                [
                    new BackupRun
                    {
                        Id = 9,
                        BackupPolicyId = 5,
                        ServerId = 20,
                        Status = BackupRunStatus.Succeeded,
                        SizeBytes = 1024,
                        StartedAt = NowUtc,
                        CompletedAt = NowUtc.AddMinutes(2),
                        RestoreCheckStatus = RestoreCheckStatus.Verified,
                        RestoreCheckMessage = "restored on a throwaway target",
                        Message = "ok"
                    }
                ],
                1));

        var page = await _service.GetRunsAsync(
            5,
            new PaginationRequest { Search = "needle", SortBy = "StartedAt", SortDescending = true },
            Ct);

        var run = Assert.Single(page.Items);
        Assert.Equal(9, run.Id);
        Assert.Equal(BackupRunStatus.Succeeded, run.Status);
        Assert.Equal(1024, run.SizeBytes);
        Assert.Equal(RestoreCheckStatus.Verified, run.RestoreCheckStatus);
        Assert.Equal("restored on a throwaway target", run.RestoreCheckMessage);
    }

    [Fact]
    public async Task GetOwningProjectIdAsync_AnswersFromThePolicyOrNull()
    {
        _repo.FindPolicyAsync(1, Arg.Any<CancellationToken>()).Returns(Policy());

        Assert.Equal(10, await _service.GetOwningProjectIdAsync(1, Ct));
        Assert.Null(await _service.GetOwningProjectIdAsync(404, Ct));
    }

    // ---------- update ----------

    [Fact]
    public async Task UpdateAsync_ReturnsNullForAnUnknownPolicyWithoutAuditingAnything()
    {
        Assert.Null(await _service.UpdateAsync(404, UpdateRequest(), Ct));

        await _audit.DidNotReceive().LogAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_TrimsTheNameAndWritesEveryEditableField()
    {
        var policy = Policy();
        _repo.FindPolicyAsync(1, Arg.Any<CancellationToken>()).Returns(policy);

        var dto = await _service.UpdateAsync(1, UpdateRequest(filePaths: ["/srv/new"]), Ct);

        Assert.Equal("renamed", policy.Name);
        Assert.False(policy.Enabled);
        Assert.Equal(BackupDbEngine.MySql, policy.DbEngine);
        Assert.Equal("db.internal", policy.DbHost);
        Assert.Equal(3306, policy.DbPort);
        Assert.Equal("0 4 * * *", policy.ScheduleCron);
        Assert.Equal(30, policy.RetentionCount);
        Assert.Equal(["/srv/new"], dto!.FilePaths);
        await _notifier.Received().BroadcastOperationalAsync(
            ResourceType.Project, 10, Arg.Any<string>(), Arg.Any<CancellationToken>(), 4);
    }

    [Fact]
    public async Task UpdateAsync_KeepsTheStoredPasswordWhenTheRequestOmitsIt()
    {
        var policy = Policy();
        _repo.FindPolicyAsync(1, Arg.Any<CancellationToken>()).Returns(policy);

        await _service.UpdateAsync(1, UpdateRequest(password: null), Ct);

        Assert.Equal("enc:secret", policy.DbPasswordEncrypted);
    }

    [Fact]
    public async Task UpdateAsync_ClearsTheStoredPasswordOnAnEmptyString()
    {
        var policy = Policy();
        _repo.FindPolicyAsync(1, Arg.Any<CancellationToken>()).Returns(policy);

        await _service.UpdateAsync(1, UpdateRequest(password: ""), Ct);

        Assert.Null(policy.DbPasswordEncrypted);
    }

    [Fact]
    public async Task UpdateAsync_ReplacesTheStoredPasswordWithAFreshCipherText()
    {
        var policy = Policy();
        _repo.FindPolicyAsync(1, Arg.Any<CancellationToken>()).Returns(policy);

        await _service.UpdateAsync(1, UpdateRequest(password: "rotated"), Ct);

        Assert.Equal("enc:rotated", policy.DbPasswordEncrypted);
    }

    [Fact]
    public async Task UpdateAsync_ClearsTheFilePathsWhenTheRequestSendsNone()
    {
        var policy = Policy();
        _repo.FindPolicyAsync(1, Arg.Any<CancellationToken>()).Returns(policy);

        await _service.UpdateAsync(1, UpdateRequest(), Ct);

        Assert.Null(policy.FilePathsJson);
    }

    [Fact]
    public async Task UpdateAsync_RejectsAnInvalidCronBeforeTouchingThePolicy()
    {
        var policy = Policy();
        _repo.FindPolicyAsync(1, Arg.Any<CancellationToken>()).Returns(policy);

        await Assert.ThrowsAsync<BadRequestException>(
            () => _service.UpdateAsync(1, UpdateRequest(cron: "not a cron"), Ct));

        Assert.Equal("nightly", policy.Name);
    }

    // ---------- delete ----------

    [Fact]
    public async Task DeleteAsync_RemovesTheKnownPolicyAndBroadcastsItsProject()
    {
        var policy = Policy();
        _repo.FindPolicyAsync(1, Arg.Any<CancellationToken>()).Returns(policy);

        Assert.True(await _service.DeleteAsync(1, Ct));

        _repo.Received().DeletePolicy(policy);
        await _audit.Received().LogAsync(
            "Deleted", "BackupPolicy", 1, "nightly", Arg.Any<CancellationToken>());
        await _notifier.Received().BroadcastOperationalAsync(
            ResourceType.Project, 10, Arg.Any<string>(), Arg.Any<CancellationToken>(), 4);
    }

    [Fact]
    public async Task DeleteAsync_ReportsAnUnknownPolicyWithoutRemovingAnything()
    {
        Assert.False(await _service.DeleteAsync(404, Ct));

        _repo.DidNotReceive().DeletePolicy(Arg.Any<BackupPolicy>());
    }

    // ---------- run now ----------

    [Fact]
    public async Task RunNowAsync_RefusesAnUnknownPolicy()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _service.RunNowAsync(404, Ct));
    }

    [Fact]
    public async Task RunNowAsync_RefusesWhenTheTargetServerNoLongerExists()
    {
        _repo.FindPolicyAsync(1, Arg.Any<CancellationToken>()).Returns(Policy());

        await Assert.ThrowsAsync<NotFoundException>(() => _service.RunNowAsync(1, Ct));
    }

    [Fact]
    public async Task RunNowAsync_RefusesWhenTheAgentIsOffline()
    {
        _repo.FindPolicyAsync(1, Arg.Any<CancellationToken>()).Returns(Policy());
        _serverRepo.FindServerAsync(20, Arg.Any<CancellationToken>())
            .Returns(new Server { Id = 20, Name = "vps-1", Status = ServerStatus.Offline });

        await Assert.ThrowsAsync<ConflictException>(() => _service.RunNowAsync(1, Ct));

        await _serverRepo.DidNotReceive().AddTaskAsync(Arg.Any<ServerTask>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunNowAsync_DispatchesTheBackupWhenTheAgentIsOnline()
    {
        var policy = Policy();
        _repo.FindPolicyAsync(1, Arg.Any<CancellationToken>()).Returns(policy);
        _serverRepo.FindServerAsync(20, Arg.Any<CancellationToken>())
            .Returns(new Server { Id = 20, Name = "vps-1", Status = ServerStatus.Online });
        BackupRun? run = null;
        _repo.When(repository => repository.AddRun(Arg.Any<BackupRun>()))
            .Do(call => run = call.Arg<BackupRun>());

        await _service.RunNowAsync(1, Ct);

        Assert.Equal(BackupRunStatus.Running, run!.Status);
        Assert.Equal(NowUtc, run.StartedAt);
        Assert.Equal(NowUtc, policy.LastRunAt);
        await _serverRepo.Received().AddTaskAsync(
            Arg.Is<ServerTask>(task => task.Operation == OperationKind.BackupExecute && task.ServerId == 20),
            Arg.Any<CancellationToken>());
        await _taskService.Received().NotifyTaskQueuedAsync(
            Arg.Any<ServerTask>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    // ---------- restore check ----------

    [Fact]
    public async Task TriggerRestoreCheckAsync_DoesNothingForARunWithNoArchiveToVerify()
    {
        await _service.TriggerRestoreCheckAsync(
            new BackupRun { Id = 9, BackupPolicyId = 1, ServerId = 20 }, Policy(), Ct);

        await _serverRepo.DidNotReceive().AddTaskAsync(Arg.Any<ServerTask>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TriggerRestoreCheckAsync_DispatchesTheCheckAndStampsThePolicy()
    {
        var policy = Policy();
        var run = new BackupRun
        {
            Id = 9,
            BackupPolicyId = 1,
            ServerId = 20,
            ArchivePath = "/var/backups/nightly-9.tar.gz"
        };

        await _service.TriggerRestoreCheckAsync(run, policy, Ct);

        Assert.Equal(NowUtc, policy.LastRestoreCheckAt);
        await _serverRepo.Received().AddTaskAsync(
            Arg.Is<ServerTask>(task =>
                task.Operation == OperationKind.BackupRestoreCheck
                && task.ServerId == 20
                && task.Command == "9"),
            Arg.Any<CancellationToken>());
        await _notifier.Received().BroadcastOperationalAsync(
            ResourceType.Project, 10, Arg.Any<string>(), Arg.Any<CancellationToken>(), 4);
    }

    [Fact]
    public async Task ApplyBackupResultAsync_RefusesAnUnknownRun()
    {
        Assert.False(await _service.ApplyBackupResultAsync(
            404, 20, new BackupExecuteResultDto { Success = true }, Ct));
    }

    [Fact]
    public async Task ApplyBackupResultAsync_RecordsAFailureWithItsMessage()
    {
        var run = new BackupRun { Id = 9, BackupPolicyId = 1, ServerId = 20, Status = BackupRunStatus.Running };
        _repo.FindRunAsync(9, Arg.Any<CancellationToken>()).Returns(run);
        _repo.FindPolicyAsync(1, Arg.Any<CancellationToken>()).Returns(Policy());

        Assert.True(await _service.ApplyBackupResultAsync(
            9, 20, new BackupExecuteResultDto { Success = false, Message = "pg_dump exit 1" }, Ct));

        Assert.Equal(BackupRunStatus.Failed, run.Status);
        Assert.Equal("pg_dump exit 1", run.Message);
        Assert.Equal(NowUtc, run.CompletedAt);
    }

    [Fact]
    public async Task ApplyRestoreCheckResultAsync_SkipsTheBroadcastWhenThePolicyIsAlreadyGone()
    {
        var run = new BackupRun { Id = 9, BackupPolicyId = 1, ServerId = 20 };
        _repo.FindRunAsync(9, Arg.Any<CancellationToken>()).Returns(run);

        Assert.True(await _service.ApplyRestoreCheckResultAsync(
            9, 20, new RestoreCheckResultDto { Verified = true }, Ct));

        Assert.Equal(RestoreCheckStatus.Verified, run.RestoreCheckStatus);
        await _notifier.DidNotReceive().BroadcastOperationalAsync(
            Arg.Any<ResourceType>(), Arg.Any<int>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>(), Arg.Any<int?>());
    }
}
