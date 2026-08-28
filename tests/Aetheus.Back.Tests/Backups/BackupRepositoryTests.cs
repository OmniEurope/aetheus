// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.AppBackups;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.Tests.Backups;

/// <summary>
/// The backup repository's read model: the project scoping the RBAC layer relies on, the free-text
/// search over the policy and its owner labels, every sort key of both paged listings, and the
/// deferred-write contract (<c>Add*</c> stages, only <c>SaveChangesAsync</c> commits).
/// </summary>
public sealed class BackupRepositoryTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly BackupRepository _repository;

    public BackupRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _repository = new BackupRepository(_db);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateTime Origin = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private async Task SaveAsync() => await _db.SaveChangesAsync(Ct);

    private static BackupPolicy Policy(
        int id, string name, int projectId = 10, int serverId = 20, bool enabled = true,
        BackupDbEngine engine = BackupDbEngine.Postgres, string cron = "0 2 * * *",
        int retention = 7, DateTime? lastRunAt = null) =>
        new()
        {
            Id = id,
            Name = name,
            ProjectId = projectId,
            ServerId = serverId,
            Enabled = enabled,
            DbEngine = engine,
            ScheduleCron = cron,
            RetentionCount = retention,
            LastRunAt = lastRunAt
        };

    private static BackupRun Run(
        int id, int policyId, BackupRunStatus status = BackupRunStatus.Succeeded,
        DateTime? startedAt = null, long sizeBytes = 0, string? message = null,
        RestoreCheckStatus restoreCheck = RestoreCheckStatus.Unverified) =>
        new()
        {
            Id = id,
            BackupPolicyId = policyId,
            ServerId = 20,
            Status = status,
            StartedAt = startedAt ?? Origin,
            SizeBytes = sizeBytes,
            Message = message,
            RestoreCheckStatus = restoreCheck
        };

    /// <summary>
    /// Three projects and three servers whose names sort differently from the policy names, so an
    /// owner-key sort that silently fell back to the name would fail instead of passing.
    /// </summary>
    private async Task SeedOwnersAsync()
    {
        _db.Projects.AddRange(
            new Project { Id = 10, Name = "alpha-project", OrganizationId = 7 },
            new Project { Id = 11, Name = "zulu-project", OrganizationId = 8 },
            new Project { Id = 12, Name = "mid-project", OrganizationId = 9 });
        _db.Servers.AddRange(
            new Server { Id = 20, Name = "alpha-server" },
            new Server { Id = 21, Name = "mid-server" },
            new Server { Id = 22, Name = "zulu-server" });
        await SaveAsync();
    }

    // ---------- policies ----------

    [Fact]
    public async Task AddPolicy_StagesTheInsertUntilSaveChangesIsCalled()
    {
        _repository.AddPolicy(Policy(1, "nightly"));

        Assert.Null(await _repository.FindPolicyAsync(1, Ct));

        await _repository.SaveChangesAsync(Ct);
        _db.ChangeTracker.Clear();

        Assert.Equal("nightly", (await _repository.FindPolicyAsync(1, Ct))!.Name);
    }

    [Fact]
    public async Task DeletePolicy_RemovesTheRowOnTheNextSave()
    {
        _repository.AddPolicy(Policy(1, "nightly"));
        await _repository.SaveChangesAsync(Ct);

        _repository.DeletePolicy((await _repository.FindPolicyAsync(1, Ct))!);
        await _repository.SaveChangesAsync(Ct);
        _db.ChangeTracker.Clear();

        Assert.Null(await _repository.FindPolicyAsync(1, Ct));
    }

    [Fact]
    public async Task GetPoliciesPagedAsync_RestrictsToTheAccessibleProjects()
    {
        await SeedOwnersAsync();
        _db.BackupPolicies.AddRange(
            Policy(1, "visible", projectId: 10),
            Policy(2, "hidden", projectId: 11));
        await SaveAsync();
        _db.ChangeTracker.Clear();

        var (items, total) = await _repository.GetPoliciesPagedAsync(
            [10], null, null, false, 1, 10, Ct);

        Assert.Equal(1, total);
        Assert.Equal(["visible"], items.Select(policy => policy.Name));
    }

    [Fact]
    public async Task GetPoliciesPagedAsync_WithoutAScopeReturnsEveryPolicy()
    {
        await SeedOwnersAsync();
        _db.BackupPolicies.AddRange(
            Policy(1, "one", projectId: 10),
            Policy(2, "two", projectId: 11));
        await SaveAsync();
        _db.ChangeTracker.Clear();

        var (_, total) = await _repository.GetPoliciesPagedAsync(null, null, null, false, 1, 10, Ct);

        Assert.Equal(2, total);
    }

    [Fact]
    public async Task GetPoliciesPagedAsync_SearchesTheNameTheProjectAndTheServer()
    {
        await SeedOwnersAsync();
        _db.BackupPolicies.AddRange(
            Policy(1, "needle-name", projectId: 11, serverId: 21),
            Policy(2, "by-project", projectId: 10, serverId: 21),
            Policy(3, "by-server", projectId: 11, serverId: 20),
            Policy(4, "unrelated", projectId: 11, serverId: 21));
        await SaveAsync();
        _db.ChangeTracker.Clear();

        var byName = await _repository.GetPoliciesPagedAsync(null, "needle", null, false, 1, 10, Ct);
        var byProject = await _repository.GetPoliciesPagedAsync(null, "alpha-project", null, false, 1, 10, Ct);
        var byServer = await _repository.GetPoliciesPagedAsync(null, "alpha-server", null, false, 1, 10, Ct);

        Assert.Equal(["needle-name"], byName.Items.Select(policy => policy.Name));
        Assert.Equal(["by-project"], byProject.Items.Select(policy => policy.Name));
        Assert.Equal(["by-server"], byServer.Items.Select(policy => policy.Name));
    }

    [Fact]
    public async Task GetPoliciesPagedAsync_MaterializesTheProjectAndServerOfEachRow()
    {
        await SeedOwnersAsync();
        _db.BackupPolicies.Add(Policy(1, "nightly"));
        await SaveAsync();
        _db.ChangeTracker.Clear();

        var (items, _) = await _repository.GetPoliciesPagedAsync(null, null, null, false, 1, 10, Ct);

        var policy = Assert.Single(items);
        Assert.Equal("alpha-project", policy.Project.Name);
        Assert.Equal("alpha-server", policy.Server.Name);
    }

    [Theory]
    [InlineData(null, false, new[] { "aaa", "mmm", "zzz" })]
    [InlineData(null, true, new[] { "zzz", "mmm", "aaa" })]
    [InlineData("UnknownKey", false, new[] { "aaa", "mmm", "zzz" })]
    [InlineData("ProjectName", false, new[] { "mmm", "zzz", "aaa" })]
    [InlineData("ProjectName", true, new[] { "aaa", "zzz", "mmm" })]
    [InlineData("ServerName", false, new[] { "zzz", "aaa", "mmm" })]
    [InlineData("ServerName", true, new[] { "mmm", "aaa", "zzz" })]
    [InlineData("DbEngine", false, new[] { "mmm", "zzz", "aaa" })]
    [InlineData("DbEngine", true, new[] { "aaa", "zzz", "mmm" })]
    [InlineData("ScheduleCron", false, new[] { "aaa", "mmm", "zzz" })]
    [InlineData("ScheduleCron", true, new[] { "zzz", "mmm", "aaa" })]
    [InlineData("RetentionCount", false, new[] { "mmm", "zzz", "aaa" })]
    [InlineData("RetentionCount", true, new[] { "aaa", "zzz", "mmm" })]
    [InlineData("LastRunAt", false, new[] { "aaa", "mmm", "zzz" })]
    [InlineData("LastRunAt", true, new[] { "zzz", "mmm", "aaa" })]
    public async Task GetPoliciesPagedAsync_SortsOnEverySupportedKey(
        string? sortBy, bool descending, string[] expected)
    {
        await SeedSortablePoliciesAsync();

        var (items, _) = await _repository.GetPoliciesPagedAsync(
            null, null, sortBy, descending, 1, 10, Ct);

        Assert.Equal(expected, items.Select(policy => policy.Name));
    }

    [Fact]
    public async Task GetPoliciesPagedAsync_SortingOnEnabledPartitionsTheDisabledPoliciesFirst()
    {
        await SeedSortablePoliciesAsync();

        var ascending = await _repository.GetPoliciesPagedAsync(null, null, "Enabled", false, 1, 10, Ct);
        var descending = await _repository.GetPoliciesPagedAsync(null, null, "Enabled", true, 1, 10, Ct);

        Assert.Equal("mmm", ascending.Items[0].Name);
        Assert.Equal("mmm", descending.Items[^1].Name);
    }

    /// <summary>
    /// Three policies whose owner names, engines, crons, retentions and last-run instants each order
    /// differently from their own names, so every sort key is distinguishable and free of ties.
    /// </summary>
    private async Task SeedSortablePoliciesAsync()
    {
        await SeedOwnersAsync();
        _db.BackupPolicies.AddRange(
            new BackupPolicy
            {
                Id = 1, Name = "aaa", ProjectId = 11, ServerId = 21, Enabled = true,
                DbEngine = BackupDbEngine.MySql, ScheduleCron = "0 1 * * *",
                RetentionCount = 30, LastRunAt = Origin
            },
            new BackupPolicy
            {
                Id = 2, Name = "mmm", ProjectId = 10, ServerId = 22, Enabled = false,
                DbEngine = BackupDbEngine.None, ScheduleCron = "0 2 * * *",
                RetentionCount = 7, LastRunAt = Origin.AddDays(1)
            },
            new BackupPolicy
            {
                Id = 3, Name = "zzz", ProjectId = 12, ServerId = 20, Enabled = true,
                DbEngine = BackupDbEngine.Postgres, ScheduleCron = "0 3 * * *",
                RetentionCount = 14, LastRunAt = Origin.AddDays(2)
            });
        await SaveAsync();
        _db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task GetPoliciesPagedAsync_PagesAfterOrderingAndKeepsTheUnpagedTotal()
    {
        await SeedOwnersAsync();
        _db.BackupPolicies.AddRange(Enumerable.Range(1, 5)
            .Select(index => Policy(index, $"policy-{index:D2}")));
        await SaveAsync();
        _db.ChangeTracker.Clear();

        var (items, total) = await _repository.GetPoliciesPagedAsync(null, null, null, false, 3, 2, Ct);

        Assert.Equal(5, total);
        Assert.Equal(["policy-05"], items.Select(policy => policy.Name));
    }

    [Fact]
    public async Task FindPolicyAsync_ReturnsATrackedPolicyOrNull()
    {
        _db.BackupPolicies.Add(Policy(1, "nightly"));
        await SaveAsync();
        _db.ChangeTracker.Clear();

        var policy = await _repository.FindPolicyAsync(1, Ct);
        policy!.Name = "renamed";
        await _repository.SaveChangesAsync(Ct);
        _db.ChangeTracker.Clear();

        Assert.Equal("renamed", (await _repository.FindPolicyAsync(1, Ct))!.Name);
        Assert.Null(await _repository.FindPolicyAsync(404, Ct));
    }

    [Fact]
    public async Task GetProjectOrganizationIdAsync_ReadsTheOwnerOrNullForAnUnknownProject()
    {
        await SeedOwnersAsync();

        Assert.Equal(7, await _repository.GetProjectOrganizationIdAsync(10, Ct));
        Assert.Null(await _repository.GetProjectOrganizationIdAsync(404, Ct));
    }

    [Fact]
    public async Task GetEnabledPoliciesAsync_SkipsTheDisabledOnes()
    {
        _db.BackupPolicies.AddRange(
            Policy(1, "on"),
            Policy(2, "off", enabled: false));
        await SaveAsync();
        _db.ChangeTracker.Clear();

        var enabled = await _repository.GetEnabledPoliciesAsync(Ct);

        Assert.Equal(["on"], enabled.Select(policy => policy.Name));
    }

    // ---------- runs ----------

    [Fact]
    public async Task AddRun_StagesTheInsertUntilSaveChangesIsCalled()
    {
        _repository.AddRun(Run(1, 5));

        Assert.Null(await _repository.FindRunAsync(1, Ct));

        await _repository.SaveChangesAsync(Ct);
        _db.ChangeTracker.Clear();

        Assert.Equal(5, (await _repository.FindRunAsync(1, Ct))!.BackupPolicyId);
    }

    [Fact]
    public async Task FindRunWithPolicyAsync_LoadsTheOwningPolicy()
    {
        _db.BackupPolicies.Add(Policy(5, "nightly"));
        _db.BackupRuns.Add(Run(1, 5));
        await SaveAsync();
        _db.ChangeTracker.Clear();

        var run = await _repository.FindRunWithPolicyAsync(1, Ct);

        Assert.Equal("nightly", run!.BackupPolicy.Name);
        Assert.Null(await _repository.FindRunWithPolicyAsync(404, Ct));
    }

    [Fact]
    public async Task GetRunsForPolicyPagedAsync_KeepsOnlyTheRunsOfThatPolicy()
    {
        _db.BackupRuns.AddRange(
            Run(1, 5),
            Run(2, 5),
            Run(3, 6));
        await SaveAsync();
        _db.ChangeTracker.Clear();

        var (items, total) = await _repository.GetRunsForPolicyPagedAsync(
            5, null, null, false, 1, 10, Ct);

        Assert.Equal(2, total);
        Assert.Equal([1, 2], items.Select(run => run.Id).Order());
    }

    [Fact]
    public async Task GetRunsForPolicyPagedAsync_SearchesTheMessageAndIgnoresRunsWithoutOne()
    {
        _db.BackupRuns.AddRange(
            Run(1, 5, message: "dump failed: needle"),
            Run(2, 5, message: "all good"),
            Run(3, 5));
        await SaveAsync();
        _db.ChangeTracker.Clear();

        var (items, total) = await _repository.GetRunsForPolicyPagedAsync(
            5, "needle", null, false, 1, 10, Ct);

        Assert.Equal(1, total);
        Assert.Equal([1], items.Select(run => run.Id));
    }

    [Theory]
    [InlineData(null, false, new[] { 3, 2, 1 })]
    [InlineData("StartedAt", false, new[] { 1, 2, 3 })]
    [InlineData("StartedAt", true, new[] { 3, 2, 1 })]
    [InlineData("Status", false, new[] { 2, 1, 3 })]
    [InlineData("Status", true, new[] { 3, 1, 2 })]
    [InlineData("SizeBytes", false, new[] { 3, 1, 2 })]
    [InlineData("SizeBytes", true, new[] { 2, 1, 3 })]
    [InlineData("RestoreCheckStatus", false, new[] { 1, 3, 2 })]
    [InlineData("RestoreCheckStatus", true, new[] { 2, 3, 1 })]
    public async Task GetRunsForPolicyPagedAsync_SortsOnEverySupportedKey(
        string? sortBy, bool descending, int[] expected)
    {
        _db.BackupRuns.AddRange(
            Run(1, 5, BackupRunStatus.Succeeded, Origin, sizeBytes: 200,
                restoreCheck: RestoreCheckStatus.Unverified),
            Run(2, 5, BackupRunStatus.Running, Origin.AddHours(1), sizeBytes: 300,
                restoreCheck: RestoreCheckStatus.Failed),
            Run(3, 5, BackupRunStatus.Failed, Origin.AddHours(2), sizeBytes: 100,
                restoreCheck: RestoreCheckStatus.Verified));
        await SaveAsync();
        _db.ChangeTracker.Clear();

        var (items, _) = await _repository.GetRunsForPolicyPagedAsync(
            5, null, sortBy, descending, 1, 10, Ct);

        Assert.Equal(expected, items.Select(run => run.Id));
    }

    [Fact]
    public async Task GetRunsForPolicyPagedAsync_PagesAfterOrdering()
    {
        _db.BackupRuns.AddRange(Enumerable.Range(1, 5)
            .Select(index => Run(index, 5, startedAt: Origin.AddHours(index))));
        await SaveAsync();
        _db.ChangeTracker.Clear();

        var (items, total) = await _repository.GetRunsForPolicyPagedAsync(
            5, null, "StartedAt", false, 2, 2, Ct);

        Assert.Equal(5, total);
        Assert.Equal([3, 4], items.Select(run => run.Id));
    }

    [Fact]
    public async Task GetLatestSuccessfulRunAsync_IgnoresNewerFailuresAndOtherPolicies()
    {
        _db.BackupRuns.AddRange(
            Run(1, 5, BackupRunStatus.Succeeded, Origin),
            Run(2, 5, BackupRunStatus.Succeeded, Origin.AddHours(1)),
            Run(3, 5, BackupRunStatus.Failed, Origin.AddHours(2)),
            Run(4, 6, BackupRunStatus.Succeeded, Origin.AddHours(3)));
        await SaveAsync();
        _db.ChangeTracker.Clear();

        var latest = await _repository.GetLatestSuccessfulRunAsync(5, Ct);

        Assert.Equal(2, latest!.Id);
    }

    [Fact]
    public async Task GetLatestSuccessfulRunAsync_ReturnsNullWhenNoRunEverSucceeded()
    {
        _db.BackupRuns.Add(Run(1, 5, BackupRunStatus.Failed, Origin));
        await SaveAsync();
        _db.ChangeTracker.Clear();

        Assert.Null(await _repository.GetLatestSuccessfulRunAsync(5, Ct));
    }

    public void Dispose() => _db.Dispose();
}
