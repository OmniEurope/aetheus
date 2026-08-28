// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Pipelines;
using Aetheus.Back.Components.Tasks;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.Constants;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aetheus.Back.Tests;

public class PipelineRepositoryQueryTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly PipelineRepository _repo;

    public PipelineRepositoryQueryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _repo = new PipelineRepository(
            _db,
            TimeProvider.System,
            NullLogger<PipelineRepository>.Instance,
            new PipelineTaskLifecycleRepository(_db, TimeProvider.System),
            new PipelineRunLineageRepository(_db));
    }

    public void Dispose() => _db.Dispose();

    private void Step(int runId, TaskExecutionStatus status, bool system = false, bool continueOnError = false,
        string? outputs = null)
    {
        _db.PipelineStepRuns.Add(new PipelineStepRun
        {
            PipelineRunId = runId,
            Status = status,
            IsSystem = system,
            ContinueOnError = continueOnError,
            StageName = "Build",
            StepName = "compile",
            OutputVariablesJson = outputs
        });
        _db.SaveChanges();
    }

    [Fact]
    public async Task GetRunMetricsAsync_ReturnsMetricsForRunOrdered()
    {
        _db.RunMetrics.AddRange(
            new RunMetric { PipelineRunId = 1, CreatedAt = new DateTime(2026, 6, 2, 0, 0, 0, DateTimeKind.Utc) },
            new RunMetric { PipelineRunId = 1, CreatedAt = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc) },
            new RunMetric { PipelineRunId = 2, CreatedAt = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc) });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var metrics = await _repo.GetRunMetricsAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, metrics.Count);
        Assert.All(metrics, m => Assert.Equal(1, m.PipelineRunId));
    }

    [Fact]
    public async Task GetCoverageResultsAsync_FiltersByRun()
    {
        _db.CoverageResults.AddRange(
            new CoverageResult { PipelineRunId = 1, CreatedAt = DateTime.UtcNow },
            new CoverageResult { PipelineRunId = 2, CreatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Single(await _repo.GetCoverageResultsAsync(1, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetLintResultsAsync_FiltersByRun()
    {
        _db.LintResults.AddRange(
            new LintResult { PipelineRunId = 1, CreatedAt = DateTime.UtcNow },
            new LintResult { PipelineRunId = 1, CreatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, (await _repo.GetLintResultsAsync(1, ct: TestContext.Current.CancellationToken)).Count);
    }

    [Fact]
    public async Task GetRunsPagedAsync_HydratesWarningsAfterEfProjection()
    {
        var pipeline = new Pipeline { Name = "warnings", YamlDefinition = "name: warnings\nstages: []" };
        _db.Pipelines.Add(pipeline);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
        _db.PipelineRuns.Add(new PipelineRun
        {
            PipelineId = pipeline.Id,
            Status = PipelineStatus.Failed,
            StartedAt = DateTime.UtcNow,
            WarningsJson = """["No online matching agent."]"""
        });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var (runs, _) = await _repo.GetRunsPagedAsync(
            pipeline.Id, 1, 25, ct: TestContext.Current.CancellationToken);

        Assert.Equal(["No online matching agent."], Assert.Single(runs).Warnings);
        Assert.Null(runs[0].ProjectedWarningsJson);
    }

    /// <summary>The runs grid is server-paged, so a sort the server ignores is a sort that does not
    /// exist: the header moves, the same page comes back. Duration is the case that hides best, since
    /// the column used to sort on CompletedAt and therefore ordered by end date while showing length.
    /// </summary>
    [Fact]
    public async Task GetRunsPagedAsync_SortsByDuration_IncludingARunStillGoing()
    {
        var pipeline = new Pipeline { Name = "durations", YamlDefinition = "name: durations\nstages: []" };
        _db.Pipelines.Add(pipeline);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var start = DateTime.UtcNow.AddHours(-4);
        // Finished last but shortest, so an order by CompletedAt cannot pass for an order by duration.
        _db.PipelineRuns.Add(new PipelineRun
        {
            PipelineId = pipeline.Id, Status = PipelineStatus.Success,
            StartedAt = start.AddHours(3), CompletedAt = start.AddHours(3).AddMinutes(2)
        });
        _db.PipelineRuns.Add(new PipelineRun
        {
            PipelineId = pipeline.Id, Status = PipelineStatus.Success,
            StartedAt = start, CompletedAt = start.AddMinutes(30)
        });
        // Still running for ~4h: measured against now, it is the longest of the three.
        _db.PipelineRuns.Add(new PipelineRun
        {
            PipelineId = pipeline.Id, Status = PipelineStatus.Running, StartedAt = start
        });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var (longestFirst, _) = await _repo.GetRunsPagedAsync(
            pipeline.Id, 1, 25,
            new PipelineRunPaginationRequest { SortBy = "duration", SortDescending = true },
            TestContext.Current.CancellationToken);

        Assert.Equal(
            [PipelineStatus.Running, PipelineStatus.Success, PipelineStatus.Success],
            longestFirst.Select(r => r.Status));
        Assert.Equal(30, (longestFirst[1].CompletedAt!.Value - longestFirst[1].StartedAt).TotalMinutes, 0);
        Assert.Equal(2, (longestFirst[2].CompletedAt!.Value - longestFirst[2].StartedAt).TotalMinutes, 0);

        var (shortestFirst, _) = await _repo.GetRunsPagedAsync(
            pipeline.Id, 1, 25,
            new PipelineRunPaginationRequest { SortBy = "duration", SortDescending = false },
            TestContext.Current.CancellationToken);

        Assert.Equal(PipelineStatus.Running, shortestFirst[^1].Status);
    }

    [Fact]
    public async Task GetRunsPagedAsync_FiltersBeforeCounting_SoThePagerMatchesWhatIsReachable()
    {
        var pipeline = new Pipeline { Name = "filters", YamlDefinition = "name: filters\nstages: []" };
        _db.Pipelines.Add(pipeline);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        _db.PipelineRuns.Add(new PipelineRun
        {
            PipelineId = pipeline.Id, Status = PipelineStatus.Failed,
            StartedAt = DateTime.UtcNow, BranchName = "develop", CommitHash = "abc123"
        });
        _db.PipelineRuns.Add(new PipelineRun
        {
            PipelineId = pipeline.Id, Status = PipelineStatus.Success,
            StartedAt = DateTime.UtcNow, BranchName = "main", CommitHash = "def456"
        });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var (failed, failedTotal) = await _repo.GetRunsPagedAsync(
            pipeline.Id, 1, 25,
            new PipelineRunPaginationRequest { Status = PipelineStatus.Failed },
            TestContext.Current.CancellationToken);
        Assert.Equal(1, failedTotal);
        Assert.Equal("develop", Assert.Single(failed).BranchName);

        var (onBranch, branchTotal) = await _repo.GetRunsPagedAsync(
            pipeline.Id, 1, 25,
            new PipelineRunPaginationRequest { BranchName = "MAIN" },
            TestContext.Current.CancellationToken);
        Assert.Equal(1, branchTotal);
        Assert.Equal("main", Assert.Single(onBranch).BranchName);

        var (byCommit, commitTotal) = await _repo.GetRunsPagedAsync(
            pipeline.Id, 1, 25,
            new PipelineRunPaginationRequest { CommitHash = "abc" },
            TestContext.Current.CancellationToken);
        Assert.Equal(1, commitTotal);
        Assert.Equal("abc123", Assert.Single(byCommit).CommitHash);
    }

    /// <summary>An unknown sort key must fall back to the default order, never reach the database as
    /// an expression built from user input.</summary>
    [Fact]
    public async Task GetRunsPagedAsync_IgnoresAnUnknownSortKey()
    {
        var pipeline = new Pipeline { Name = "hostile", YamlDefinition = "name: hostile\nstages: []" };
        _db.Pipelines.Add(pipeline);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var newest = DateTime.UtcNow;
        _db.PipelineRuns.Add(new PipelineRun { PipelineId = pipeline.Id, Status = PipelineStatus.Success, StartedAt = newest.AddHours(-1) });
        _db.PipelineRuns.Add(new PipelineRun { PipelineId = pipeline.Id, Status = PipelineStatus.Success, StartedAt = newest });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var (runs, _) = await _repo.GetRunsPagedAsync(
            pipeline.Id, 1, 25,
            new PipelineRunPaginationRequest { SortBy = "1; drop table pipeline_runs" },
            TestContext.Current.CancellationToken);

        Assert.True(runs[0].StartedAt > runs[1].StartedAt);
    }

    [Fact]
    public async Task IsRunStillRunningAsync_ReflectsStatus()
    {
        _db.PipelineRuns.Add(new PipelineRun { Id = 1, PipelineId = 1, Status = PipelineStatus.Running });
        _db.PipelineRuns.Add(new PipelineRun { Id = 2, PipelineId = 1, Status = PipelineStatus.Success });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(await _repo.IsRunStillRunningAsync(1, ct: TestContext.Current.CancellationToken));
        Assert.False(await _repo.IsRunStillRunningAsync(2, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task HasAnyFailedStepInRunAsync_IgnoresContinueOnError()
    {
        Step(1, TaskExecutionStatus.Failed, continueOnError: true);
        Assert.False(await _repo.HasAnyFailedStepInRunAsync(1, ct: TestContext.Current.CancellationToken));

        Step(1, TaskExecutionStatus.Failed, continueOnError: false);
        Assert.True(await _repo.HasAnyFailedStepInRunAsync(1, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task HasAnySucceededStepInRunAsync_IgnoresSystemSteps()
    {
        Step(1, TaskExecutionStatus.Success, system: true);
        Assert.False(await _repo.HasAnySucceededStepInRunAsync(1, ct: TestContext.Current.CancellationToken));

        Step(1, TaskExecutionStatus.Success, system: false);
        Assert.True(await _repo.HasAnySucceededStepInRunAsync(1, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetSuccessfulStepOutputsAsync_ReturnsOnlyStepsWithOutputs()
    {
        Step(1, TaskExecutionStatus.Success, outputs: "{\"key\":\"val\"}");
        Step(1, TaskExecutionStatus.Success, outputs: null);
        Step(1, TaskExecutionStatus.Failed, outputs: "{\"x\":\"y\"}");

        var outputs = await _repo.GetSuccessfulStepOutputsAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.Single(outputs);
    }

    [Fact]
    public async Task GetSuccessfulStepOutputsAsync_OrdersOutputsByPipelineStepOrder()
    {
        _db.PipelineStepRuns.AddRange(
            new PipelineStepRun
            {
                PipelineRunId = 1,
                StageName = "PromoteCurrent",
                StepName = "Current candidate",
                Order = 2,
                Status = TaskExecutionStatus.Success,
                OutputVariablesJson = "{\"CANDIDATE_VERSION\":\"c-current\"}"
            },
            new PipelineStepRun
            {
                PipelineRunId = 1,
                StageName = "Baseline",
                StepName = "Baseline candidate",
                Order = 1,
                Status = TaskExecutionStatus.Success,
                OutputVariablesJson = "{\"CANDIDATE_VERSION\":\"c-baseline\"}"
            });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var outputs = await _repo.GetSuccessfulStepOutputsAsync(
            1,
            ct: TestContext.Current.CancellationToken);

        Assert.Equal(["Baseline", "PromoteCurrent"], outputs.Select(output => output.StageName));
    }

    [Fact]
    public async Task GetComplexityTrendAsync_RunNotFound_ReturnsEmpty()
        => Assert.Empty(await _repo.GetComplexityTrendAsync(999, 10, ct: TestContext.Current.CancellationToken));

    [Fact]
    public async Task GetComplexityTrendAsync_ReturnsTrendRowForRunWithMetrics()
    {
        var run = new PipelineRun { Id = 1, PipelineId = 5, Status = PipelineStatus.Success, StartedAt = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc) };
        _db.PipelineRuns.Add(run);
        _db.RunMetrics.AddRange(
            new RunMetric { PipelineRunId = 1, PipelineRun = run, Key = "complexity.cyclomatic.avg", Value = 3.5 },
            new RunMetric { PipelineRunId = 1, PipelineRun = run, Key = "complexity.cyclomatic.max", Value = 12 },
            new RunMetric { PipelineRunId = 1, PipelineRun = run, Key = "complexity.crap.avg", Value = 8.2 });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var trend = await _repo.GetComplexityTrendAsync(1, 10, ct: TestContext.Current.CancellationToken);

        Assert.Single(trend);
        Assert.Equal(3.5, trend[0].AvgCyclomatic);
        Assert.Equal(12, trend[0].MaxCyclomatic);
    }

    [Fact]
    public async Task GetPipelineIdsWithActiveRunsAsync_ReturnsDistinctActivePipelineIds()
    {
        _db.PipelineRuns.AddRange(
            new PipelineRun { Id = 1, PipelineId = 10, Status = PipelineStatus.Running },
            new PipelineRun { Id = 2, PipelineId = 10, Status = PipelineStatus.Pending },
            new PipelineRun { Id = 3, PipelineId = 11, Status = PipelineStatus.Success });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var ids = await _repo.GetPipelineIdsWithActiveRunsAsync(ct: TestContext.Current.CancellationToken);

        Assert.Contains(10, ids);
        Assert.DoesNotContain(11, ids);
    }

    [Fact]
    public async Task GetStalledSchedulableRunIdsAsync_ReturnsOnlyOldPendingRunsWithoutInflightWork()
    {
        var now = new DateTime(2026, 7, 18, 18, 0, 0, DateTimeKind.Utc);
        var stalledWithDeferredCleanup = new PipelineRun
        {
            Id = 1,
            PipelineId = 10,
            Status = PipelineStatus.Running,
            StartedAt = now.AddMinutes(-10)
        };
        stalledWithDeferredCleanup.Tasks.Add(new ServerTask
        {
            Server = new Server { Name = "cleanup-runner", Hostname = "cleanup-runner" },
            Name = "deferred cleanup",
            Command = "cleanup",
            Status = TaskExecutionStatus.Pending,
            IsDeferredCleanup = true,
            CreatedAt = now.AddMinutes(-10)
        });
        _db.PipelineRuns.AddRange(
            stalledWithDeferredCleanup,
            new PipelineRun { Id = 2, PipelineId = 10, Status = PipelineStatus.Running, StartedAt = now.AddSeconds(-30) },
            new PipelineRun { Id = 3, PipelineId = 10, Status = PipelineStatus.Running, StartedAt = now.AddMinutes(-10) },
            new PipelineRun { Id = 4, PipelineId = 10, Status = PipelineStatus.Success, StartedAt = now.AddMinutes(-10) });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);
        Step(1, TaskExecutionStatus.Pending);
        Step(2, TaskExecutionStatus.Pending);
        Step(3, TaskExecutionStatus.Pending);
        Step(3, TaskExecutionStatus.Running);
        Step(4, TaskExecutionStatus.Pending);

        var ids = await _repo.GetStalledSchedulableRunIdsAsync(
            now.AddMinutes(-2), TestContext.Current.CancellationToken);

        Assert.Equal([1], ids);
    }

    [Fact]
    public async Task GetStalledCancellationRunIdsAsync_ReturnsOnlyOldRequestedRunsWithoutInflightWork()
    {
        var now = new DateTime(2026, 8, 8, 20, 0, 0, DateTimeKind.Utc);
        var cancellationJson = $"{{\"{PipelineRunService.CancellationRequestedVariable}\":\"true\"}}";
        var stalled = new PipelineRun
        {
            Id = 10,
            PipelineId = 10,
            Status = PipelineStatus.Running,
            StartedAt = now.AddMinutes(-10),
            AdditionalVariablesJson = cancellationJson
        };
        stalled.Tasks.Add(new ServerTask
        {
            Server = new Server { Name = "cleanup-runner", Hostname = "cleanup-runner" },
            Name = "deferred cleanup",
            Command = "cleanup",
            Status = TaskExecutionStatus.Pending,
            IsDeferredCleanup = true,
            CreatedAt = now.AddMinutes(-10)
        });
        var active = new PipelineRun
        {
            Id = 11,
            PipelineId = 10,
            Status = PipelineStatus.Running,
            StartedAt = now.AddMinutes(-10),
            AdditionalVariablesJson = cancellationJson
        };
        active.Tasks.Add(new ServerTask
        {
            Server = new Server { Name = "active-runner", Hostname = "active-runner" },
            Name = "active work",
            Command = "work",
            Status = TaskExecutionStatus.Running,
            CreatedAt = now.AddMinutes(-10)
        });
        _db.PipelineRuns.AddRange(
            stalled,
            active,
            new PipelineRun
            {
                Id = 12,
                PipelineId = 10,
                Status = PipelineStatus.Running,
                StartedAt = now.AddMinutes(-10),
                AdditionalVariablesJson = "{}"
            },
            new PipelineRun
            {
                Id = 13,
                PipelineId = 10,
                Status = PipelineStatus.Running,
                StartedAt = now.AddSeconds(-30),
                AdditionalVariablesJson = cancellationJson
            });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var ids = await _repo.GetStalledCancellationRunIdsAsync(
            now.AddMinutes(-2), TestContext.Current.CancellationToken);

        Assert.Equal([10], ids);
    }

    [Fact]
    public async Task HasAnyRunningStepInRunAsync_TrueWhenRunningStep()
    {
        Step(1, TaskExecutionStatus.Running);
        Assert.True(await _repo.HasAnyRunningStepInRunAsync(1, ct: TestContext.Current.CancellationToken));
        Assert.False(await _repo.HasAnyRunningStepInRunAsync(2, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetWebhookTriggeredPipelinesForProjectAsync_ReturnsOnlyWebhookPipelines()
    {
        _db.Pipelines.AddRange(
            new Pipeline { Id = 1, Name = "hook", ProjectId = 1, TriggerType = PipelineTriggerType.Webhook },
            new Pipeline { Id = 2, Name = "manual", ProjectId = 1, TriggerType = PipelineTriggerType.Manual });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetWebhookTriggeredPipelinesForProjectAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.Single(result);
        Assert.Equal("hook", result[0].Name);
    }

    [Fact]
    public async Task GetEnvironmentCopyTargetAsync_ReturnsNameAndProject()
    {
        _db.Environments.Add(new Aetheus.Back.Data.Entities.Environment { Id = 1, Name = "prod", ProjectId = 9 });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var target = await _repo.GetEnvironmentCopyTargetAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(target);
        Assert.Equal("prod", target.Value.Name);
        Assert.Equal(9, target.Value.ProjectId);
        Assert.Null(await _repo.GetEnvironmentCopyTargetAsync(999, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FindServerIdsInPoolAsync_ReturnsServerIdsForPool()
    {
        _db.AgentPoolServers.Add(new AgentPoolServer { ServerId = 7, AgentPool = new AgentPool { Id = 1, Name = "build-pool" } });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var ids = await _repo.FindServerIdsInPoolAsync("build-pool", ct: TestContext.Current.CancellationToken);

        Assert.Contains(7, ids);
    }

    [Fact]
    public async Task FindServerIdsInEnvironmentAsync_ReturnsServerIdsForEnvironment()
    {
        _db.EnvironmentServers.Add(new EnvironmentServer { ServerId = 8, Environment = new Aetheus.Back.Data.Entities.Environment { Id = 1, Name = "staging" } });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var ids = await _repo.FindServerIdsInEnvironmentAsync("staging", ct: TestContext.Current.CancellationToken);

        Assert.Contains(8, ids);
    }

    [Fact]
    public async Task FindOnlineServerByIdAsync_ReturnsOnlyEnabledOnlineRunner()
    {
        _db.Servers.AddRange(
            new Server { Id = 1, Name = "on", Hostname = "on", Status = ServerStatus.Online, PipelineRunnerEnabled = true, AgentProtocolVersion = AgentProtocol.CurrentVersion, AgentCapabilitiesJson = "[\"pipeline.build\"]" },
            new Server { Id = 2, Name = "off", Hostname = "off", Status = ServerStatus.Offline, PipelineRunnerEnabled = true, AgentProtocolVersion = AgentProtocol.CurrentVersion, AgentCapabilitiesJson = "[\"pipeline.build\"]" },
            new Server { Id = 3, Name = "inventory", Hostname = "inventory", Status = ServerStatus.Online, PipelineRunnerEnabled = false },
            new Server { Id = 4, Name = "previous-protocol", Hostname = "previous-protocol", Status = ServerStatus.Online, PipelineRunnerEnabled = true, AgentProtocolVersion = AgentProtocol.CurrentVersion - 1, AgentCapabilitiesJson = "[\"pipeline.build\"]" });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(await _repo.FindOnlineServerByIdAsync(1, ct: TestContext.Current.CancellationToken));
        Assert.Null(await _repo.FindOnlineServerByIdAsync(2, ct: TestContext.Current.CancellationToken));
        Assert.Null(await _repo.FindOnlineServerByIdAsync(3, ct: TestContext.Current.CancellationToken));
        Assert.Null(await _repo.FindOnlineServerByIdAsync(4, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FindCandidateTargetServerIdsAsync_NoSelectorIncludesOnlyOrganizationFallbackRunners()
    {
        _db.Servers.AddRange(
            new Server { Id = 1, Name = "org-runner", Hostname = "one", OrganizationId = 10, PipelineRunnerEnabled = true },
            new Server { Id = 2, Name = "foreign-runner", Hostname = "two", OrganizationId = 20, PipelineRunnerEnabled = true },
            new Server { Id = 3, Name = "not-runner", Hostname = "three", OrganizationId = 10, PipelineRunnerEnabled = false });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var ids = await _repo.FindCandidateTargetServerIdsAsync(
            null, null, null, OsType.Unknown, 10, deploymentStage: false,
            ct: TestContext.Current.CancellationToken);

        Assert.Equal([1], ids);
    }

    [Fact]
    public async Task FindCandidateTargetServerIdsAsync_MissingExplicitRunnerSelectorDoesNotFallback()
    {
        _db.Servers.Add(new Server
        {
            Id = 1,
            Name = "org-runner",
            Hostname = "one",
            OrganizationId = 10,
            PipelineRunnerEnabled = true
        });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var ids = await _repo.FindCandidateTargetServerIdsAsync(
            null, null, "missing", OsType.Unknown, 10, deploymentStage: false,
            ct: TestContext.Current.CancellationToken);

        Assert.Empty(ids);
    }

    [Fact]
    public async Task FindCandidateTargetServerIdsAsync_MissingExplicitDeploySelectorDoesNotFallback()
    {
        _db.Servers.Add(new Server
        {
            Id = 1,
            Name = "prod",
            Hostname = "prod",
            OrganizationId = 10,
            DeploymentTargetAvailable = true
        });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var ids = await _repo.FindCandidateTargetServerIdsAsync(
            null, "missing", null, OsType.Unknown, 10, deploymentStage: true, ct: TestContext.Current.CancellationToken);

        Assert.Empty(ids);
    }

    [Fact]
    public async Task ExplicitAgentSelector_IsScopedToPipelineOrganization()
    {
        _db.Servers.AddRange(
            new Server { Id = 1, Name = "shared", Hostname = "foreign", OrganizationId = 20, Status = ServerStatus.Online, PipelineRunnerEnabled = true, AgentProtocolVersion = AgentProtocol.CurrentVersion, AgentCapabilitiesJson = "[\"pipeline.build\"]" },
            new Server { Id = 2, Name = "shared", Hostname = "owned", OrganizationId = 10, Status = ServerStatus.Online, PipelineRunnerEnabled = true, AgentProtocolVersion = AgentProtocol.CurrentVersion, AgentCapabilitiesJson = "[\"pipeline.build\"]" });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var server = await _repo.FindOnlineServerByAgentInOrganizationAsync("shared", OsType.Unknown, 10, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(server);
        Assert.Equal(2, server.Id);
    }

    [Fact]
    public async Task ExplicitPoolSelector_IsScopedToPipelineOrganization()
    {
        var pool = new AgentPool { Id = 1, Name = "shared-pool" };
        var foreign = new Server { Id = 1, Name = "foreign", Hostname = "foreign", OrganizationId = 20, Status = ServerStatus.Online, PipelineRunnerEnabled = true, AgentProtocolVersion = AgentProtocol.CurrentVersion, AgentCapabilitiesJson = "[\"pipeline.build\"]" };
        var owned = new Server { Id = 2, Name = "owned", Hostname = "owned", OrganizationId = 10, Status = ServerStatus.Online, PipelineRunnerEnabled = true, AgentProtocolVersion = AgentProtocol.CurrentVersion, AgentCapabilitiesJson = "[\"pipeline.build\"]" };
        _db.AgentPoolServers.AddRange(
            new AgentPoolServer { AgentPool = pool, Server = foreign },
            new AgentPoolServer { AgentPool = pool, Server = owned });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var server = await _repo.FindOnlineServerInPoolInOrganizationAsync("shared-pool", OsType.Unknown, 10, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(server);
        Assert.Equal(2, server.Id);
    }

    [Fact]
    public async Task ExplicitDeployEnvironment_IsScopedToPipelineOrganization()
    {
        var environment = new Aetheus.Back.Data.Entities.Environment { Id = 1, Name = "prod" };
        var foreign = new Server { Id = 1, Name = "foreign", Hostname = "foreign", OrganizationId = 20, Status = ServerStatus.Online, DeploymentTargetAvailable = true, AgentProtocolVersion = AgentProtocol.CurrentVersion, AgentCapabilitiesJson = "[\"deployment.apply\"]" };
        var owned = new Server { Id = 2, Name = "owned", Hostname = "owned", OrganizationId = 10, Status = ServerStatus.Online, DeploymentTargetAvailable = true, AgentProtocolVersion = AgentProtocol.CurrentVersion, AgentCapabilitiesJson = "[\"deployment.apply\"]" };
        _db.EnvironmentServers.AddRange(
            new EnvironmentServer { Environment = environment, Server = foreign },
            new EnvironmentServer { Environment = environment, Server = owned });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var server = await _repo.FindOnlineDeployTargetAsync(null, "prod", null, OsType.Unknown, 10, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(server);
        Assert.Equal(2, server.Id);
    }

    [Fact]
    public async Task GetStageProducerServerIdAsync_ReturnsSuccessfulServerFromRequestedStage()
    {
        _db.PipelineStepRuns.AddRange(
            new PipelineStepRun { Id = 1, PipelineRunId = 7, StageName = "build", StepName = "compile", ServerId = 11, Status = TaskExecutionStatus.Success },
            new PipelineStepRun { Id = 2, PipelineRunId = 7, StageName = "deploy", StepName = "ship", ServerId = 22, Status = TaskExecutionStatus.Success });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(11, await _repo.GetStageProducerServerIdAsync(7, "build", ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetPipelinesByEnvironmentAsync_FiltersByEnvironment()
    {
        _db.Pipelines.AddRange(
            new Pipeline { Id = 1, Name = "a", EnvironmentId = 5 },
            new Pipeline { Id = 2, Name = "b", EnvironmentId = 6 });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Single(await _repo.GetPipelinesByEnvironmentAsync(5, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetPipelineIdForArtifactAsync_ResolvesThroughRun()
    {
        var run = new PipelineRun { Id = 1, PipelineId = 42, Status = PipelineStatus.Success };
        _db.PipelineArtifacts.Add(new PipelineArtifact { Id = 1, PipelineRunId = 1, PipelineId = 42, Name = "a", FilePath = "/a", PipelineRun = run });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(42, await _repo.GetPipelineIdForArtifactAsync(1, ct: TestContext.Current.CancellationToken));
        Assert.Null(await _repo.GetPipelineIdForArtifactAsync(999, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetPipelineIdForApprovalAsync_ResolvesThroughRun()
    {
        var run = new PipelineRun { Id = 1, PipelineId = 55, Status = PipelineStatus.WaitingForApproval };
        _db.PipelineApprovals.Add(new PipelineApproval { Id = 1, PipelineRunId = 1, PipelineRun = run });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(55, await _repo.GetPipelineIdForApprovalAsync(1, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TryTransitionPipelineRunStatusAsync_RequiresExpectedSourceStatus()
    {
        _db.PipelineRuns.Add(new PipelineRun { Id = 1, PipelineId = 55, Status = PipelineStatus.WaitingForApproval });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(await _repo.TryTransitionPipelineRunStatusAsync(
            1, PipelineStatus.Running, PipelineStatus.Cancelled, ct: TestContext.Current.CancellationToken));
        Assert.True(await _repo.TryTransitionPipelineRunStatusAsync(
            1, PipelineStatus.WaitingForApproval, PipelineStatus.Cancelled, ct: TestContext.Current.CancellationToken));
        Assert.Equal(PipelineStatus.Cancelled, (await _db.PipelineRuns.FindAsync([1], TestContext.Current.CancellationToken))!.Status);
    }

    [Fact]
    public async Task GetTriggeredChildRunIdsAsync_ReturnsDistinctDirectChildrenOnly()
    {
        _db.PipelineStepRuns.AddRange(
            new PipelineStepRun { PipelineRunId = 1, StageName = "release", StepName = "ci", TriggeredRunId = 2 },
            new PipelineStepRun { PipelineRunId = 1, StageName = "release", StepName = "ci-again", TriggeredRunId = 2 },
            new PipelineStepRun { PipelineRunId = 1, StageName = "release", StepName = "qa", TriggeredRunId = 3 },
            new PipelineStepRun { PipelineRunId = 2, StageName = "ci", StepName = "nested", TriggeredRunId = 4 });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var children = await _repo.GetTriggeredChildRunIdsAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.Equal([2, 3], children.OrderBy(id => id));
    }

    [Fact]
    public async Task GetActiveRunIdsAsync_ReturnsOnlyNonTerminalRunsForPipeline()
    {
        _db.PipelineRuns.AddRange(
            new PipelineRun { Id = 1, PipelineId = 10, Status = PipelineStatus.Running },
            new PipelineRun { Id = 2, PipelineId = 10, Status = PipelineStatus.WaitingForApproval },
            new PipelineRun { Id = 3, PipelineId = 10, Status = PipelineStatus.Success },
            new PipelineRun { Id = 4, PipelineId = 11, Status = PipelineStatus.Running });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var ids = await _repo.GetActiveRunIdsAsync(10, TestContext.Current.CancellationToken);

        Assert.Equal([1, 2], ids);
    }

    [Fact]
    public async Task CancellationRequest_CancelsOrdinaryTailButPreservesMandatoryTeardown()
    {
        _db.PipelineRuns.Add(new PipelineRun
        {
            Id = 1,
            PipelineId = 55,
            Status = PipelineStatus.Running,
            AdditionalVariablesJson = "{}"
        });
        var workStep = new PipelineStepRun
        {
            PipelineRunId = 1,
            StageName = "Work",
            StepName = "pending",
            Status = TaskExecutionStatus.Pending
        };
        var teardownStep = new PipelineStepRun
        {
            PipelineRunId = 1,
            StageName = "Teardown",
            StepName = "cleanup",
            Status = TaskExecutionStatus.Pending
        };
        var systemCleanupStep = new PipelineStepRun
        {
            PipelineRunId = 1,
            StageName = PipelineRunService.SystemCleanupStage,
            StepName = "workspace",
            Status = TaskExecutionStatus.Pending,
            IsSystem = true
        };
        var racedStep = new PipelineStepRun
        {
            PipelineRunId = 1,
            StageName = "Work",
            StepName = "started-during-cancellation",
            Status = TaskExecutionStatus.Running
        };
        var orphanStep = new PipelineStepRun
        {
            PipelineRunId = 1,
            StageName = "Work",
            StepName = "running-without-task",
            Status = TaskExecutionStatus.Running
        };
        _db.PipelineStepRuns.AddRange(workStep, teardownStep, systemCleanupStep, racedStep, orphanStep);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
        _db.Tasks.AddRange(
            new ServerTask
            {
                ServerId = 1,
                PipelineRunId = 1,
                PipelineStepRunId = workStep.Id,
                Name = "work",
                Status = TaskExecutionStatus.Pending
            },
            new ServerTask
            {
                ServerId = 1,
                PipelineRunId = 1,
                PipelineStepRunId = teardownStep.Id,
                Name = "teardown",
                Status = TaskExecutionStatus.Pending
            },
            new ServerTask
            {
                ServerId = 1,
                PipelineRunId = 1,
                PipelineStepRunId = null,
                Name = "Collect Artifacts (BuildArtifacts)",
                Operation = OperationKind.PipelineCollectArtifacts,
                Status = TaskExecutionStatus.Pending
            },
            new ServerTask
            {
                ServerId = 1,
                PipelineRunId = 1,
                PipelineStepRunId = racedStep.Id,
                Name = "started-during-cancellation",
                Status = TaskExecutionStatus.Cancelled
            });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        await _repo.CancelPendingStepRunsExceptStagesAsync(
            1,
            ["Teardown", PipelineRunService.SystemCleanupStage],
            TestContext.Current.CancellationToken);

        Assert.Equal(TaskExecutionStatus.Running, orphanStep.Status);

        await _repo.CancelOrphanedRunningStepRunsExceptStagesAsync(
            1,
            ["Teardown", PipelineRunService.SystemCleanupStage],
            TestContext.Current.CancellationToken);

        var variables = PipelineRunHelpers.DeserializeResolvedVariables(
            (await _db.PipelineRuns.FindAsync([1], TestContext.Current.CancellationToken))!.AdditionalVariablesJson);
        Assert.Equal("true", variables[PipelineRunService.CancellationRequestedVariable]);
        var steps = await _db.PipelineStepRuns
            .OrderBy(step => step.Id)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(TaskExecutionStatus.Cancelled, steps.Single(step => step.StepName == "pending").Status);
        Assert.Equal(
            TaskExecutionStatus.Cancelled,
            steps.Single(step => step.StepName == "started-during-cancellation").Status);
        Assert.Equal(
            TaskExecutionStatus.Cancelled,
            steps.Single(step => step.StepName == "running-without-task").Status);
        Assert.Equal(TaskExecutionStatus.Pending, steps.Single(step => step.StageName == "Teardown").Status);
        Assert.Equal(
            TaskExecutionStatus.Pending,
            steps.Single(step => step.StageName == PipelineRunService.SystemCleanupStage).Status);
        var tasks = await _db.Tasks.OrderBy(task => task.Id)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(TaskExecutionStatus.Cancelled, tasks.Single(task => task.Name == "work").Status);
        Assert.Equal(TaskExecutionStatus.Pending, tasks.Single(task => task.Name == "teardown").Status);
        Assert.Equal(
            TaskExecutionStatus.Cancelled,
            tasks.Single(task => task.Operation == OperationKind.PipelineCollectArtifacts).Status);
    }

    [Fact]
    public async Task DependencyQueries_ProjectFiveNewestRuns()
    {
        _db.Pipelines.Add(new Pipeline { Id = 70, Name = "history" });
        _db.PipelineRuns.AddRange(Enumerable.Range(1, 6).Select(id => new PipelineRun
        {
            Id = id,
            PipelineId = 70,
            Status = id % 2 == 0 ? PipelineStatus.Success : PipelineStatus.Failed,
            StartedAt = new DateTime(2026, 7, 1, 0, id, 0, DateTimeKind.Utc)
        }));
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var graphItem = Assert.Single(await _repo.GetPipelinesForDependencyGraphAsync(ct: TestContext.Current.CancellationToken));
        var (pageItems, _, _) = await _repo.GetPipelineDependencyPageAsync(
            new PipelinePaginationRequest { Page = 1, PageSize = 25 }, ct: TestContext.Current.CancellationToken);
        var pageItem = Assert.Single(pageItems);

        Assert.Equal([6, 5, 4, 3, 2], graphItem.RecentRuns.Select(run => run.Id));
        Assert.Equal([6, 5, 4, 3, 2], pageItem.RecentRuns.Select(run => run.Id));
    }

    [Fact]
    public async Task DependencyGraph_ServerScope_ReturnsOnlyPipelinesExecutedOnServer()
    {
        _db.Pipelines.AddRange(
            new Pipeline { Id = 80, Name = "used", YamlDefinition = "name: used\nstages: []" },
            new Pipeline { Id = 81, Name = "unused", YamlDefinition = "name: unused\nstages: []" });
        _db.PipelineRuns.Add(new PipelineRun { Id = 80, PipelineId = 80, Status = PipelineStatus.Success });
        _db.PipelineStepRuns.Add(new PipelineStepRun
        {
            PipelineRunId = 80,
            ServerId = 12,
            StepName = "deploy",
            Status = TaskExecutionStatus.Success
        });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var pipelines = await _repo.GetPipelinesForDependencyGraphAsync(
            serverId: 12, ct: TestContext.Current.CancellationToken);

        Assert.Equal("used", Assert.Single(pipelines).Name);
    }

    [Fact]
    public async Task TryResolveApprovalAsync_OnlyFirstPendingDecisionWins()
    {
        var run = new PipelineRun { Id = 1, PipelineId = 55, Status = PipelineStatus.WaitingForApproval };
        var environment = new Aetheus.Back.Data.Entities.Environment { Id = 1, Name = "production" };
        _db.PipelineRuns.Add(run);
        _db.Environments.Add(environment);
        _db.PipelineApprovals.Add(new PipelineApproval
        {
            Id = 1,
            PipelineRunId = 1,
            PipelineRun = run,
            EnvironmentId = 1,
            Environment = environment,
            StageName = "deploy",
            Status = ApprovalStatus.Pending
        });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var first = await _repo.TryResolveApprovalAsync(
            1, ApprovalStatus.Approved, DateTime.UtcNow, null, "approved", ct: TestContext.Current.CancellationToken);
        var second = await _repo.TryResolveApprovalAsync(
            1, ApprovalStatus.Rejected, DateTime.UtcNow, null, "rejected", ct: TestContext.Current.CancellationToken);

        Assert.NotNull(first);
        Assert.Equal(ApprovalStatus.Approved, first.Status);
        Assert.Null(second);
    }
}
