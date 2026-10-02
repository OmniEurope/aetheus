// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Audit;
using Aetheus.Back.Components.Git;
using Aetheus.Back.Components.Pipelines;
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Exceptions;
using Aetheus.Back.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Aetheus.Back.Tests.Pipelines;

/// <summary>
/// A `type: trigger` step that cannot start its child must FAIL, out loud.
///
/// This came from a real wedge on the mirror: `publish-observability-packages` passed the package
/// version to its children as a variable, while each child declares `version` as a required
/// parameter. The launch threw "Parameter 'version' is required." from outside the one try/catch in
/// this method, so the exception unwound the entire scheduling pass. The run kept the status
/// Running with every step Pending - no step marked, no warning recorded - and the reconcile sweep
/// re-threw on it once a minute, for as long as the backend stayed up. A run that reports nothing
/// forever is worse than a failed one, because nobody is told to look at it.
/// </summary>
public sealed class PipelineTriggerStepCoordinatorFailureTests
{
    private readonly IPipelineRepository _repo = Substitute.For<IPipelineRepository>();
    private readonly IPipelineGitService _git = Substitute.For<IPipelineGitService>();
    private readonly IPipelineRunPreparationService _preparations = Substitute.For<IPipelineRunPreparationService>();
    private readonly IPipelineCheckpointReuseService _checkpoints = Substitute.For<IPipelineCheckpointReuseService>();
    private readonly IResourceAuthorizationService _authz = Substitute.For<IResourceAuthorizationService>();
    private readonly IAuditService _audit = Substitute.For<IAuditService>();
    private readonly IPipelineChildRunLauncher _launcher = Substitute.For<IPipelineChildRunLauncher>();
    private readonly PipelineTriggerStepCoordinator _sut;

    private const int ParentRunId = 1118;
    private const int ProjectId = 13;

    public PipelineTriggerStepCoordinatorFailureTests()
    {
        _sut = new PipelineTriggerStepCoordinator(
            _repo, _git, _preparations, _checkpoints, _authz, _audit,
            new FakeTimeProvider(),
            NullLogger<PipelineTriggerStepCoordinator>.Instance);

        var parentPipeline = new Pipeline { Id = 77, Name = "publish-observability-packages", ProjectId = ProjectId };
        var parentRun = new PipelineRun { Id = ParentRunId, PipelineId = parentPipeline.Id, Pipeline = parentPipeline };
        var childPipeline = new Pipeline
        {
            Id = 74,
            Name = "package-aetheus-telemetry",
            ProjectId = ProjectId,
            CreatedByUsername = "admin",
        };

        _repo.GetPipelineRunWithPipelineAsync(ParentRunId, Arg.Any<CancellationToken>()).Returns(parentRun);
        _repo.GetPipelineProjectIdAsync(parentPipeline, Arg.Any<CancellationToken>()).Returns(ProjectId);
        _repo.FindPipelineByNameAndProjectAsync("package-aetheus-telemetry", ProjectId, Arg.Any<CancellationToken>())
            .Returns(childPipeline);
        _preparations.PrepareRunAsync(
                childPipeline.Id, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new PipelineRunPreparation
            {
                PipelineId = childPipeline.Id,
                YamlSnapshot = "name: package-aetheus-telemetry",
                TargetServerIds = [],
            });
        _authz.HasPermissionAsync(
                Arg.Any<string>(), Arg.Any<ResourceType>(), Arg.Any<int>(), Arg.Any<Permission>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _checkpoints.TryReuseCheckpointAsync(
                Arg.Any<PipelineRun>(), Arg.Any<Pipeline>(), Arg.Any<string>(), Arg.Any<PipelineRunPreparation>(),
                Arg.Any<Dictionary<string, string>>(), Arg.Any<PipelineStepRun>(), Arg.Any<DateTime>(),
                Arg.Any<CancellationToken>())
            .Returns(false);
    }

    private static PipelineStepRun Step() => new()
    {
        Id = 9510,
        PipelineRunId = ParentRunId,
        StageName = "TelemetryCandidate",
        StepName = "Validate Telemetry candidate",
        Status = TaskExecutionStatus.Pending,
    };

    private static PipelineStepDefinition Definition() => new()
    {
        Name = "Validate Telemetry candidate",
        Type = "trigger",
        Pipeline = "package-aetheus-telemetry",
    };

    /// <summary>
    /// The exact wedge: the child refuses the launch because a required parameter was not supplied.
    /// The step must end Failed with the child's own words, and the run must carry a warning, so the
    /// pipeline reports the refusal instead of silently spinning.
    /// </summary>
    [Fact]
    public async Task ARefusedChildLaunch_FailsTheStepInsteadOfWedgingTheRun()
    {
        _launcher.TriggerPreparedRunAsync(
                Arg.Any<PipelineRunPreparation>(), Arg.Any<Dictionary<string, string>>(),
                Arg.Any<Dictionary<string, string>?>(), Arg.Any<CancellationToken>(), Arg.Any<string>())
            .Throws(new BadRequestException("Parameter 'version' is required."));

        var step = Step();

        // Not Assert.ThrowsAsync's opposite by accident: an unhandled exception here unwinds the
        // whole scheduling pass, which is precisely the wedge this test exists to prevent.
        var exception = await Record.ExceptionAsync(() => _sut.CreateTriggerStepAsync(
            ParentRunId, step, Definition(), [], _launcher, CancellationToken.None));
        Assert.Null(exception);

        Assert.Equal(TaskExecutionStatus.Failed, step.Status);
        Assert.Contains("Parameter 'version' is required.", step.FailureReason);

        await _repo.Received(1).AppendRunWarningsAsync(
            ParentRunId,
            Arg.Is<IReadOnlyCollection<string>>(w => w.Any(m => m.Contains("Parameter 'version' is required."))),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The success path is untouched by the guard: a child that starts leaves the step Running and
    /// linked to it, which is what lets the completion handler settle it later.
    /// </summary>
    [Fact]
    public async Task AnAcceptedChildLaunch_LeavesTheStepRunningAndLinked()
    {
        _launcher.TriggerPreparedRunAsync(
                Arg.Any<PipelineRunPreparation>(), Arg.Any<Dictionary<string, string>>(),
                Arg.Any<Dictionary<string, string>?>(), Arg.Any<CancellationToken>(), Arg.Any<string>())
            .Returns(new PipelineRunDto { Id = 1119 });

        var step = Step();

        await _sut.CreateTriggerStepAsync(
            ParentRunId, step, Definition(), [], _launcher, CancellationToken.None);

        Assert.Equal(TaskExecutionStatus.Running, step.Status);
        Assert.Equal(1119, step.TriggeredRunId);
    }
}
