// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Pipelines;
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Environment = Aetheus.Back.Data.Entities.Environment;

namespace Aetheus.Back.Tests.Pipelines;

/// <summary>
/// The launch-time preflight: what a run needs is asked for before anything is dispatched.
///
/// The distinction every case here turns on is "unmatchable" versus "not right now". A selector no
/// server is configured for can never resolve itself and must refuse the launch; a configured server
/// that happens to be offline is the scheduler's stand-by path and must NOT become a launch error,
/// or a transient outage would read as a broken pipeline.
/// </summary>
public sealed class PipelineRunPreflightServiceTests
{
    private readonly IPipelineRepository _repo = Substitute.For<IPipelineRepository>();

    private PipelineRunPreflightService BuildSut(IPipelineEnvironmentCheckGuard? checks = null)
        => new(
            _repo,
            new PipelineDispatchServerResolver(_repo),
            checks ?? Admits(true),
            Substitute.For<ILogger<PipelineRunPreflightService>>());

    private static IPipelineEnvironmentCheckGuard Admits(bool verdict)
    {
        var guard = Substitute.For<IPipelineEnvironmentCheckGuard>();
        guard.CheckEnvironmentChecksAsync(Arg.Any<PipelineStageDefinition>(), Arg.Any<CancellationToken>())
            .Returns(verdict);
        return guard;
    }

    private void ConfiguredTargets(params int[] ids)
        => _repo.FindCandidateTargetServerIdsAsync(
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<OsType>(),
                Arg.Any<int?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(ids.ToList());

    private static PipelineYamlDefinition Definition(params PipelineStageDefinition[] stages)
        => new() { Name = "p", Stages = [.. stages] };

    private static PipelineStageDefinition Stage(
        string name, string? agent = null, string? environment = null, params PipelineStepDefinition[] steps)
        => new()
        {
            Name = name,
            // Agent is a non-nullable string on the DTO; "no selector" is the empty string, not null.
            Agent = agent ?? string.Empty,
            Environment = environment,
            Steps = steps.Length > 0 ? [.. steps] : [new() { Name = "run", Shell = "make" }]
        };

    private Task<IReadOnlyList<string>> RunAsync(PipelineYamlDefinition definition, int? projectId = 7)
        => BuildSut().FindBlockingProblemsAsync(
            definition, new Dictionary<string, string>(), organizationId: 3, projectId, CancellationToken.None);

    [Fact]
    public async Task SelectorNoServerIsConfiguredFor_RefusesTheLaunchAndNamesTheSelector()
    {
        ConfiguredTargets();

        var problems = await RunAsync(Definition(Stage("QA", agent: "ghost-agent")));

        var problem = Assert.Single(problems);
        Assert.Contains("QA", problem, StringComparison.Ordinal);
        Assert.Contains("ghost-agent", problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConfiguredButOfflineServer_IsNotALaunchError()
    {
        // A server exists for the selector; the fleet query says nothing about it being online, and
        // the preflight must not invent that verdict. The scheduler parks the run instead.
        ConfiguredTargets(42);

        var problems = await RunAsync(Definition(Stage("QA", agent: "linux-01")));

        Assert.Empty(problems);
    }

    [Fact]
    public async Task StageWithNoSelectorAtAll_IsLeftToDispatch()
    {
        // No pool, environment or agent: the stage falls back to any runner of the organization, a
        // case the candidate-target query cannot express. Refusing here would block ordinary runs.
        ConfiguredTargets();

        var problems = await RunAsync(Definition(Stage("build")));

        Assert.Empty(problems);
    }

    [Fact]
    public async Task EnvironmentCheckThatRefuses_IsReportedAtLaunchInsteadOfHoursIn()
    {
        // The regression this service exists for: a broken QA environment check used to surface only
        // when the QA stage was finally dispatched, after CI, Quality and Security had run.
        ConfiguredTargets(42);
        var sut = BuildSut(Admits(false));

        var problems = await sut.FindBlockingProblemsAsync(
            Definition(Stage("QA", agent: "linux-01", environment: "qa")),
            new Dictionary<string, string>(), organizationId: 3, projectId: 7, CancellationToken.None);

        var problem = Assert.Single(problems);
        Assert.Contains("qa", problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TriggerStepNamingAPipelineThatDoesNotExist_RefusesTheLaunch()
    {
        ConfiguredTargets(42);
        _repo.FindPipelineByNameAndProjectAsync("aetheus-qa", 7, Arg.Any<CancellationToken>())
            .Returns((Pipeline?)null);

        var problems = await RunAsync(Definition(Stage(
            "QA", agent: "linux-01", environment: null,
            new PipelineStepDefinition { Name = "run QA", Type = "trigger", Pipeline = "aetheus-qa" })));

        var problem = Assert.Single(problems);
        Assert.Contains("aetheus-qa", problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ArtifactSourcePipelineThatExists_PassesPreflight()
    {
        ConfiguredTargets(42);
        _repo.FindPipelineByNameAndProjectAsync("aetheus-ci", 7, Arg.Any<CancellationToken>())
            .Returns(new Pipeline { Id = 1, Name = "aetheus-ci" });

        var problems = await RunAsync(Definition(Stage(
            "Restore", agent: "linux-01", environment: null,
            new PipelineStepDefinition
            {
                Name = "restore", Type = "restore-artifacts", ArtifactSourcePipeline = "aetheus-ci"
            })));

        Assert.Empty(problems);
    }

    [Fact]
    public async Task EveryUnmetRequirementIsReportedTogether_NotJustTheFirst()
    {
        // One launch, one answer: fixing them one run at a time is the slow loop this replaces.
        ConfiguredTargets();

        var problems = await RunAsync(Definition(
            Stage("QA", agent: "ghost-a"),
            Stage("Deploy", agent: "ghost-b")));

        Assert.Equal(2, problems.Count);
    }

    [Fact]
    public async Task WithoutAProject_PipelineReferencesAreLeftAlone()
    {
        // A pipeline with no project cannot resolve a name to a pipeline, so the check is skipped
        // rather than guessed. Silently refusing would block every project-less pipeline.
        ConfiguredTargets(42);

        var problems = await RunAsync(
            Definition(Stage(
                "QA", agent: "linux-01", environment: null,
                new PipelineStepDefinition { Name = "run QA", Type = "trigger", Pipeline = "aetheus-qa" })),
            projectId: null);

        Assert.Empty(problems);
    }

    [Fact]
    public async Task EnvironmentEntityIsNeverRequiredToExistForAStageWithoutOne()
    {
        ConfiguredTargets(42);
        _repo.FindEnvironmentByNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((Environment?)null);

        var problems = await RunAsync(Definition(Stage("build", agent: "linux-01")));

        Assert.Empty(problems);
    }
}
