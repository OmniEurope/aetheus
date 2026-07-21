// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Pipelines;
using Aetheus.Back.Components.Pipelines.Events;
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Aetheus.Back.Tests.Pipelines;

/// <summary>
/// The WAIT half of the parent/child (<c>type: trigger</c>) orchestration. When a child run finishes,
/// the handler mirrors its terminal status onto the still-Running parent step and re-enters
/// AdvanceStageAsync so the parent run can progress. Untested before - a regression would strand
/// parent runs Running forever.
/// </summary>
public sealed class PipelineRunCompletedTriggerHandlerTests
{
    private readonly IPipelineRepository _repo = Substitute.For<IPipelineRepository>();
    private readonly IPipelineRunService _runService = Substitute.For<IPipelineRunService>();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 7, 6, 12, 0, 0, TimeSpan.Zero));
    private readonly PipelineRunCompletedTriggerHandler _sut;

    public PipelineRunCompletedTriggerHandlerTests()
        => _sut = new PipelineRunCompletedTriggerHandler(_repo, _runService, _clock, NullLogger<PipelineRunCompletedTriggerHandler>.Instance);

    private PipelineStepRun ArrangeWaitingStep(TaskExecutionStatus status = TaskExecutionStatus.Running)
    {
        var step = new PipelineStepRun
        {
            Id = 10,
            PipelineRunId = 5,
            StageName = "deploy",
            StepName = "trigger-child",
            Status = status,
            TriggeredRunId = 99
        };
        _repo.FindStepRunsByTriggeredRunIdAsync(99, Arg.Any<CancellationToken>()).Returns([step]);
        return step;
    }

    [Fact]
    public async Task HandleAsync_ChildSuccess_MirrorsSuccessAndAdvancesStage()
    {
        var step = ArrangeWaitingStep();

        await _sut.HandleAsync(new PipelineRunCompletedEvent(99, PipelineStatus.Success), ct: TestContext.Current.CancellationToken);

        Assert.Equal(TaskExecutionStatus.Success, step.Status);
        Assert.Equal(0, step.ExitCode);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, step.CompletedAt);
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _runService.Received(1).AdvanceStageAsync(5, "deploy", Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(PipelineStatus.Failed)]
    [InlineData(PipelineStatus.Cancelled)]
    public async Task HandleAsync_ChildNotSuccess_SetsStepFailedExitCode1(PipelineStatus childStatus)
    {
        var step = ArrangeWaitingStep();

        await _sut.HandleAsync(new PipelineRunCompletedEvent(99, childStatus), ct: TestContext.Current.CancellationToken);

        Assert.Equal(TaskExecutionStatus.Failed, step.Status);
        Assert.Equal(1, step.ExitCode);
        await _runService.Received(1).AdvanceStageAsync(5, "deploy", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_StepAlreadyResolved_SkipsAndDoesNotAdvance()
    {
        ArrangeWaitingStep(status: TaskExecutionStatus.Success); // no longer Running

        await _sut.HandleAsync(new PipelineRunCompletedEvent(99, PipelineStatus.Failed), ct: TestContext.Current.CancellationToken);

        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _runService.DidNotReceive().AdvanceStageAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_NoWaitingSteps_NoOp()
    {
        _repo.FindStepRunsByTriggeredRunIdAsync(99, Arg.Any<CancellationToken>()).Returns([]);

        await _sut.HandleAsync(new PipelineRunCompletedEvent(99, PipelineStatus.Success), ct: TestContext.Current.CancellationToken);

        await _runService.DidNotReceive().AdvanceStageAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
