// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Git;
using Aetheus.Back.Components.Git.Events;
using Aetheus.Back.Components.Pipelines;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Aetheus.Back.Tests.Pipelines;

/// <summary>
/// What a git push means for pipelines, now that Git only announces it.
///
/// These assertions came with the behaviour when it moved out of GitSmartHttpService: the same
/// coverage, exercised where the decision now lives. Losing them in the move would have been the
/// worst outcome of the refactor - the trigger path is how every webhook-driven run starts.
/// </summary>
public sealed class GitPushPipelineTriggerHandlerTests
{
    private readonly IPipelineService _pipelineService = Substitute.For<IPipelineService>();
    private readonly IPipelineRunService _runService = Substitute.For<IPipelineRunService>();
    private readonly IPipelineRepository _pipelineRepo = Substitute.For<IPipelineRepository>();
    private readonly IGitLightCliService _cli = Substitute.For<IGitLightCliService>();
    private readonly GitPushPipelineTriggerHandler _sut;

    public GitPushPipelineTriggerHandlerTests() =>
        _sut = new GitPushPipelineTriggerHandler(
            _pipelineService, _runService, _pipelineRepo, _cli,
            NullLogger<GitPushPipelineTriggerHandler>.Instance);

    private static GitPushProcessedEvent Push(string reference, string commit, string defaultBranch = "main") =>
        new(7, 1, "repo", "Repo", defaultBranch, "/srv/git/repo.git", [new GitRefUpdate(reference, commit)]);

    /// <summary>
    /// Definitions are synchronised from the pushed commit BEFORE anything is triggered, so a push
    /// that edits a pipeline runs the edited one - and both use the commit the push actually
    /// accepted, not the branch tip at some later moment.
    /// </summary>
    [Fact]
    public async Task Handle_SyncsDefinitionsFromTheAcceptedCommitThenTriggers()
    {
        const string commit = "0123456789abcdef0123456789abcdef01234567";
        const string yaml = "name: release\ntrigger: webhook\nbranches:\n  - main\nstages: []";
        _cli.GetTreeAsync("/srv/git/repo.git", commit, ".pipeline", Arg.Any<CancellationToken>())
            .Returns([new GitLightTreeEntryDto { Name = "release.yml", Type = GitTreeEntryType.Blob }]);
        _cli.GetBlobAsync("/srv/git/repo.git", commit, ".pipeline/release.yml", Arg.Any<CancellationToken>())
            .Returns(new GitLightBlobDto { Path = ".pipeline/release.yml", Content = yaml });
        _pipelineService.GetWebhookTriggeredPipelinesForProjectAsync(7, Arg.Any<CancellationToken>())
            .Returns([new PipelineDto
            {
                Id = 12, Name = "release", ProjectId = 7,
                TriggerType = PipelineTriggerType.Webhook, YamlDefinition = yaml
            }]);
        _pipelineService.HasActiveRunAsync(12, Arg.Any<CancellationToken>()).Returns(false);

        await _sut.HandleAsync(Push("refs/heads/main", commit), TestContext.Current.CancellationToken);

        await _pipelineService.Received(1).UpsertPipelineFromYamlAsync(
            "release", yaml, 7, "webhook", Arg.Any<CancellationToken>(), "main", "main", 1);
        await _runService.Received(1).TriggerAutomatedRunAsync(
            12, "GitPush",
            Arg.Is<Dictionary<string, string>>(variables =>
                variables["WEBHOOK_REF"] == "refs/heads/main"
                && variables["AETHEUS_SOURCE_COMMIT"] == commit),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Latest-wins. The replacement is prepared and authorized BEFORE the running one is cancelled,
    /// so a refused run cannot leave the pipeline with nothing running at all.
    /// </summary>
    [Fact]
    public async Task Handle_SupersedeRunning_ReplacesTheActiveRun()
    {
        const string commit = "fedcba9876543210fedcba9876543210fedcba98";
        const string yaml = """
            name: candidate
            trigger: webhook
            branches:
              - develop
            supersede_running: true
            stages: []
            """;
        var pipeline = new PipelineDto
        {
            Id = 24, Name = "candidate", ProjectId = 7,
            TriggerType = PipelineTriggerType.Webhook, YamlDefinition = yaml
        };
        var preparation = new PipelineRunPreparation
        {
            PipelineId = pipeline.Id,
            YamlSnapshot = yaml,
            TargetServerIds = []
        };
        _cli.GetTreeAsync(Arg.Any<string>(), commit, ".pipeline", Arg.Any<CancellationToken>()).Returns([]);
        _pipelineService.GetWebhookTriggeredPipelinesForProjectAsync(7, Arg.Any<CancellationToken>())
            .Returns([pipeline]);
        _pipelineService.HasActiveRunAsync(pipeline.Id, Arg.Any<CancellationToken>()).Returns(true);
        _pipelineRepo.GetActiveRunIdsAsync(pipeline.Id, Arg.Any<CancellationToken>()).Returns([415]);
        _runService.PrepareAutomatedRunAsync(
                pipeline.Id, "GitPush", Arg.Any<Dictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(preparation);
        _runService.CancelRunAsync(415, Arg.Any<CancellationToken>()).Returns(true);
        _runService.TriggerPreparedRunAsync(
                preparation, Arg.Any<Dictionary<string, string>>(), Arg.Any<Dictionary<string, string>?>(),
                Arg.Any<CancellationToken>(), Arg.Any<string?>())
            .Returns(new PipelineRunDto { Id = 416, PipelineId = pipeline.Id });

        await _sut.HandleAsync(Push("refs/heads/develop", commit, "develop"), TestContext.Current.CancellationToken);

        await _runService.Received(1).PrepareAutomatedRunAsync(
            pipeline.Id, "GitPush",
            Arg.Is<Dictionary<string, string>>(variables =>
                variables["WEBHOOK_REF"] == "refs/heads/develop"
                && variables["AETHEUS_SOURCE_COMMIT"] == commit),
            Arg.Any<CancellationToken>());
        await _runService.Received(1).CancelRunAsync(415, Arg.Any<CancellationToken>());
        await _runService.Received(1).TriggerPreparedRunAsync(
            preparation, Arg.Any<Dictionary<string, string>>(), Arg.Any<Dictionary<string, string>?>(),
            Arg.Any<CancellationToken>(), Arg.Any<string?>());
        await _runService.DidNotReceive().TriggerAutomatedRunAsync(
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<Dictionary<string, string>?>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A push already written to disk must not be reported as failed because a trigger did not fire.
    /// The swallow-and-log came with the behaviour and is the reason this handler is dispatched as an
    /// observer rather than strictly.
    /// </summary>
    [Fact]
    public async Task Handle_TriggerFailure_DoesNotPropagate()
    {
        _cli.GetTreeAsync(Arg.Any<string>(), Arg.Any<string>(), ".pipeline", Arg.Any<CancellationToken>())
            .Returns([]);
        _pipelineService.GetWebhookTriggeredPipelinesForProjectAsync(7, Arg.Any<CancellationToken>())
            .Returns<List<PipelineDto>>(_ => throw new InvalidOperationException("pipeline store unavailable"));

        var exception = await Record.ExceptionAsync(() =>
            _sut.HandleAsync(Push("refs/heads/main", "abc"), TestContext.Current.CancellationToken));

        Assert.Null(exception);
    }
}
