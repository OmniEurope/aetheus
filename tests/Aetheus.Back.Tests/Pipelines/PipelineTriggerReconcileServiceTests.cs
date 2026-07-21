// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Pipelines;
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Services;
using Aetheus.Shared.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Aetheus.Back.Tests.Pipelines;

/// <summary>
/// Guards the orchestration self-heal (the fix for stuck run #49): pass 1 mirrors a terminal child onto a
/// wedged <c>type: trigger</c> step and re-enters the stage machine; pass 2 force-fails a run wedged in
/// Running with no in-flight work. Previously this service had no test coverage at all.
/// </summary>
public class PipelineTriggerReconcileServiceTests
{
    private readonly IPipelineRepository _repo = Substitute.For<IPipelineRepository>();
    private readonly IPipelineRunService _runService = Substitute.For<IPipelineRunService>();
    private readonly PipelineTriggerReconcileService _sut;

    public PipelineTriggerReconcileServiceTests()
    {
        var sp = Substitute.For<IServiceProvider>();
        sp.GetService(typeof(IPipelineRepository)).Returns(_repo);
        sp.GetService(typeof(IPipelineRunService)).Returns(_runService);
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(sp);
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);

        // Default both sweeps to "nothing to do"; each test overrides the pass it exercises.
        _repo.GetStuckRunningTriggerStepsAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns([]);
        _repo.GetStalledSchedulableRunIdsAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns([]);
        _repo.GetStuckRunningRunIdsAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns([]);

        _sut = new PipelineTriggerReconcileService(
            scopeFactory,
            NullLogger<PipelineTriggerReconcileService>.Instance,
            Options.Create(new BackgroundServicesOptions()),
            TimeProvider.System);
    }

    private static PipelineStepRun TriggerStep(int childRunId) => new()
    {
        Id = 5,
        PipelineRunId = 1,
        StageName = "orchestrate",
        StepName = "trigger-child",
        Status = TaskExecutionStatus.Running,
        TriggeredRunId = childRunId,
        StartedAt = DateTime.UtcNow.AddMinutes(-10)
    };

    [Fact]
    public async Task Pass1_ChildSucceeded_ResolvesStepToSuccessAndAdvances()
    {
        var step = TriggerStep(99);
        _repo.GetStuckRunningTriggerStepsAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns([step]);
        _repo.GetRunStatusesByIdsAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<int, PipelineStatus> { [99] = PipelineStatus.Success });

        await _sut.ReconcileAsync(TestContext.Current.CancellationToken);

        Assert.Equal(TaskExecutionStatus.Success, step.Status);
        Assert.Equal(0, step.ExitCode);
        Assert.NotNull(step.CompletedAt);
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _runService.Received(1).AdvanceStageAsync(1, "orchestrate", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Pass1_ChildFailed_ResolvesStepToFailed()
    {
        var step = TriggerStep(99);
        _repo.GetStuckRunningTriggerStepsAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns([step]);
        _repo.GetRunStatusesByIdsAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<int, PipelineStatus> { [99] = PipelineStatus.Failed });

        await _sut.ReconcileAsync(TestContext.Current.CancellationToken);

        Assert.Equal(TaskExecutionStatus.Failed, step.Status);
        Assert.Equal(1, step.ExitCode);
        await _runService.Received(1).AdvanceStageAsync(1, "orchestrate", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Pass1_ChildRunMissing_FailsStepRatherThanHang()
    {
        var step = TriggerStep(99);
        _repo.GetStuckRunningTriggerStepsAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns([step]);
        // Child row gone (deleted/retention): no entry in the status map.
        _repo.GetRunStatusesByIdsAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<int, PipelineStatus>());

        await _sut.ReconcileAsync(TestContext.Current.CancellationToken);

        Assert.Equal(TaskExecutionStatus.Failed, step.Status);
        await _runService.Received(1).AdvanceStageAsync(1, "orchestrate", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Pass1_ChildStillRunningWithinGrace_LeavesStepUntouched()
    {
        var step = TriggerStep(99);
        _repo.GetStuckRunningTriggerStepsAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns([step]);
        _repo.GetRunStatusesByIdsAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<int, PipelineStatus> { [99] = PipelineStatus.Running });

        await _sut.ReconcileAsync(TestContext.Current.CancellationToken);

        // Child not terminal and within the hard ceiling → nothing resolved, no advance.
        Assert.Equal(TaskExecutionStatus.Running, step.Status);
        await _runService.DidNotReceive().AdvanceStageAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Pass2_StuckRun_ForceFailed()
    {
        _repo.GetStuckRunningRunIdsAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns([7]);

        await _sut.ReconcileAsync(TestContext.Current.CancellationToken);

        await _runService.Received(1).FailStuckRunAsync(7, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Pass2_PendingStepsWithoutInflightWork_RedrivesScheduler()
    {
        _repo.GetStalledSchedulableRunIdsAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns([220]);

        await _sut.ReconcileAsync(CancellationToken.None);

        await _runService.Received(1).ReconcileRunAsync(220, Arg.Any<CancellationToken>());
        await _runService.DidNotReceive().FailStuckRunAsync(220, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reconcile_NothingStuck_TouchesNothing()
    {
        await _sut.ReconcileAsync(TestContext.Current.CancellationToken);

        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _runService.DidNotReceive().AdvanceStageAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _runService.DidNotReceive().ReconcileRunAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _runService.DidNotReceive().FailStuckRunAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }
}
