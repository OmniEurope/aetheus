// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Pipelines;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
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
        _repo = new PipelineRepository(_db, TimeProvider.System, NullLogger<PipelineRepository>.Instance);
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
        _db.PipelineRuns.AddRange(
            new PipelineRun { Id = 1, PipelineId = 10, Status = PipelineStatus.Running, StartedAt = now.AddMinutes(-10) },
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
    public async Task FindOnlineServerByIdAsync_ReturnsOnlyOnlineServer()
    {
        _db.Servers.AddRange(
            new Server { Id = 1, Name = "on", Hostname = "on", Status = ServerStatus.Online },
            new Server { Id = 2, Name = "off", Hostname = "off", Status = ServerStatus.Offline });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(await _repo.FindOnlineServerByIdAsync(1, ct: TestContext.Current.CancellationToken));
        Assert.Null(await _repo.FindOnlineServerByIdAsync(2, ct: TestContext.Current.CancellationToken));
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
            new Server { Id = 1, Name = "shared", Hostname = "foreign", OrganizationId = 20, Status = ServerStatus.Online, PipelineRunnerEnabled = true },
            new Server { Id = 2, Name = "shared", Hostname = "owned", OrganizationId = 10, Status = ServerStatus.Online, PipelineRunnerEnabled = true });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var server = await _repo.FindOnlineServerByAgentInOrganizationAsync("shared", OsType.Unknown, 10, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(server);
        Assert.Equal(2, server.Id);
    }

    [Fact]
    public async Task ExplicitPoolSelector_IsScopedToPipelineOrganization()
    {
        var pool = new AgentPool { Id = 1, Name = "shared-pool" };
        var foreign = new Server { Id = 1, Name = "foreign", Hostname = "foreign", OrganizationId = 20, Status = ServerStatus.Online, PipelineRunnerEnabled = true };
        var owned = new Server { Id = 2, Name = "owned", Hostname = "owned", OrganizationId = 10, Status = ServerStatus.Online, PipelineRunnerEnabled = true };
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
        var foreign = new Server { Id = 1, Name = "foreign", Hostname = "foreign", OrganizationId = 20, Status = ServerStatus.Online, DeploymentTargetAvailable = true };
        var owned = new Server { Id = 2, Name = "owned", Hostname = "owned", OrganizationId = 10, Status = ServerStatus.Online, DeploymentTargetAvailable = true };
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
