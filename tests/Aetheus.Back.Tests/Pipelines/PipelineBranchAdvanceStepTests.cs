// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Audit;
using Aetheus.Back.Components.Git;
using Aetheus.Back.Components.Pipelines;
using Aetheus.Back.Data.Entities;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Aetheus.Back.Tests.Pipelines;

/// <summary>
/// Recette R2-001: <c>type: advance-branch</c> moves a branch onto the commit of the release the run
/// deploys, as its candidate recorded it, and every outcome is visible on the step.
/// </summary>
public class PipelineBranchAdvanceStepTests
{
    private const int RunId = 42;
    private const int ProjectId = 3;
    private const string Version = "2.4.0-2483";
    private const string CandidateSha = "1111111111111111111111111111111111111111";
    private const string PreviousSha = "0000000000000000000000000000000000000000";

    private readonly IPipelineRepository _pipelines = Substitute.For<IPipelineRepository>();
    private readonly IGitBranchAdvanceService _advance = Substitute.For<IGitBranchAdvanceService>();
    private readonly IAuditService _audit = Substitute.For<IAuditService>();
    private readonly PipelineBranchAdvanceStep _sut;
    private readonly PipelineStepRun _stepRun = new() { Id = 7, PipelineRunId = RunId, StepName = "Advance main" };
    private readonly List<string> _runLines = [];

    public PipelineBranchAdvanceStepTests()
    {
        _sut = new PipelineBranchAdvanceStep(_pipelines, _advance, _audit, new FakeTimeProvider());
        var pipeline = new Pipeline { Id = 1, Name = "aetheus-deploy-prod", ProjectId = ProjectId };
        _pipelines.GetPipelineRunWithPipelineAsync(RunId, Arg.Any<CancellationToken>())
            .Returns(new PipelineRun { Id = RunId, Pipeline = pipeline, CommitHash = "2222222222222222222222222222222222222222" });
        _pipelines.GetPipelineProjectIdAsync(pipeline, Arg.Any<CancellationToken>()).Returns(ProjectId);
        _pipelines.AppendRunWarningsAsync(RunId, Arg.Do<IReadOnlyCollection<string>>(lines => _runLines.AddRange(lines)),
            Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        GivenRelease(ReleaseStatus.Deployed, CandidateSha);
    }

    private void GivenRelease(ReleaseStatus status, string? commit) =>
        _pipelines.FindBranchAdvanceReleaseAsync(ProjectId, Version, Arg.Any<CancellationToken>())
            .Returns(new BranchAdvanceRelease(9, Version, status, commit));

    private void GivenOutcome(GitBranchAdvanceOutcome outcome, string? previous = PreviousSha, string? detail = null) =>
        _advance.TryFastForwardAsync(ProjectId, "main", CandidateSha, Arg.Any<CancellationToken>())
            .Returns(new GitBranchAdvanceResult(outcome, "aetheus", previous, detail));

    private Task ExecuteAsync(string? branch = "main", string? candidateVersion = Version)
    {
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (candidateVersion is not null) variables["AETHEUS_CANDIDATE_VERSION"] = candidateVersion;
        return _sut.ExecuteAsync(
            RunId, _stepRun, new PipelineStepDefinition { Name = "Advance main", Type = "advance-branch", Branch = branch },
            variables, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Advances_TheBranch_OntoTheCommitTheCandidateRecorded_AndSaysSo()
    {
        GivenOutcome(GitBranchAdvanceOutcome.Advanced);

        await ExecuteAsync();

        // The release's commit, never the run's own workspace head (2222...).
        await _advance.Received(1).TryFastForwardAsync(ProjectId, "main", CandidateSha, Arg.Any<CancellationToken>());
        Assert.Equal(TaskExecutionStatus.Success, _stepRun.Status);
        Assert.Equal(0, _stepRun.ExitCode);
        Assert.NotNull(_stepRun.CompletedAt);
        var line = Assert.Single(_runLines);
        Assert.Contains("'main' advanced from 000000000000 to 111111111111", line, StringComparison.Ordinal);
        Assert.Contains(Version, line, StringComparison.Ordinal);
        await _audit.Received(1).LogAsync("AdvancedBranchAfterDeploy", "Release", 9,
            Arg.Is<string>(details => details.Contains(CandidateSha, StringComparison.Ordinal)), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AlreadyThere_Succeeds_WithALineSayingNothingMoved()
    {
        GivenOutcome(GitBranchAdvanceOutcome.AlreadyUpToDate, CandidateSha);

        await ExecuteAsync();

        Assert.Equal(TaskExecutionStatus.Success, _stepRun.Status);
        Assert.Contains("already points at 111111111111", Assert.Single(_runLines), StringComparison.Ordinal);
        await _audit.DidNotReceive().LogAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NotFastForward_FailsTheStep_WithTheReason()
    {
        GivenOutcome(GitBranchAdvanceOutcome.NotFastForward);

        await ExecuteAsync();

        Assert.Equal(TaskExecutionStatus.Failed, _stepRun.Status);
        Assert.Equal(TaskFailureCodes.ToolError, _stepRun.FailureCode);
        Assert.Contains("not an ancestor of 111111111111", _stepRun.FailureReason, StringComparison.Ordinal);
        Assert.Contains(_stepRun.FailureReason!, Assert.Single(_runLines), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(GitBranchAdvanceOutcome.CommitNotFound, "no internal repository of the project holds commit")]
    [InlineData(GitBranchAdvanceOutcome.Refused, "was refused: cannot lock ref")]
    public async Task CommitNotFound_OrRefused_FailsTheStep(GitBranchAdvanceOutcome outcome, string expected)
    {
        GivenOutcome(outcome, detail: "cannot lock ref");

        await ExecuteAsync();

        Assert.Equal(TaskExecutionStatus.Failed, _stepRun.Status);
        Assert.Contains(expected, _stepRun.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMissingRelease_FailsTheStep_WithoutTouchingGit()
    {
        _pipelines.FindBranchAdvanceReleaseAsync(ProjectId, Version, Arg.Any<CancellationToken>()).Returns((BranchAdvanceRelease?)null);

        await ExecuteAsync();

        Assert.Equal(TaskExecutionStatus.Failed, _stepRun.Status);
        Assert.Equal($"release '{Version}' does not exist in this project.", _stepRun.FailureReason);
        await _advance.DidNotReceiveWithAnyArgs().TryFastForwardAsync(default, default!, default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AReleaseNotYetRecordedAsDeployed_FailsTheStep()
    {
        GivenRelease(ReleaseStatus.Published, CandidateSha);

        await ExecuteAsync();

        Assert.Equal(TaskExecutionStatus.Failed, _stepRun.Status);
        Assert.Contains("not Deployed", _stepRun.FailureReason, StringComparison.Ordinal);
        await _advance.DidNotReceiveWithAnyArgs().TryFastForwardAsync(default, default!, default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ARunWithoutACandidateVersion_FailsTheStep()
    {
        await ExecuteAsync(candidateVersion: null);

        Assert.Equal(TaskExecutionStatus.Failed, _stepRun.Status);
        Assert.Contains("AETHEUS_CANDIDATE_VERSION", _stepRun.FailureReason, StringComparison.Ordinal);
        await _advance.DidNotReceiveWithAnyArgs().TryFastForwardAsync(default, default!, default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AMissingBranch_FailsTheStep()
    {
        await ExecuteAsync(branch: "  ");

        Assert.Equal(TaskExecutionStatus.Failed, _stepRun.Status);
        Assert.Equal("missing 'branch' to advance.", _stepRun.FailureReason);
    }
}
