// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Git;
using Aetheus.Back.Data.Entities;
using static Aetheus.Back.Components.Pipelines.PipelineRunHelpers;

namespace Aetheus.Back.Components.Pipelines;

/// <summary>
/// Recette R2-001: <c>type: advance-branch</c>. A branch (<c>main</c> for production) is moved by an
/// explicit, visible step of the deploy pipeline onto the commit of the release that run deploys,
/// instead of by a per-environment switch that did nothing, without a trace, while it was off.
///
/// The commit is the one the release records, i.e. the one its candidate built, never the head of the
/// deploy run's own workspace (a newer <c>develop</c> that was never deployed). The release must already
/// be recorded as deployed, so the step belongs after the <c>type: release</c> step with
/// <c>deployed: true</c>. The ref is written by the backend, which hosts the repository
/// (<see cref="IGitBranchAdvanceService"/>, ADR-044): no credential reaches the agent.
///
/// Advanced and already-there succeed and say so in the run; every other outcome fails the step with
/// the reason, because a branch that silently stays behind is the defect this replaces.
/// </summary>
public sealed class PipelineBranchAdvanceStep(
    IPipelineRepository pipelines,
    IGitBranchAdvanceService branchAdvance,
    IAuditService audit,
    TimeProvider timeProvider) : IPipelineBranchAdvanceStep
{
    /// <summary>The run variable naming the release a deploy pipeline delivers.</summary>
    internal const string CandidateVersionVariable = "AETHEUS_CANDIDATE_VERSION";

    public async Task ExecuteAsync(
        int runId, PipelineStepRun stepRun, PipelineStepDefinition stepDef,
        Dictionary<string, string> legVars, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(stepRun);
        ArgumentNullException.ThrowIfNull(stepDef);
        ArgumentNullException.ThrowIfNull(legVars);

        var target = await ResolveTargetAsync(runId, stepDef, legVars, ct).ConfigureAwait(false);
        if (target.Error is not null)
        {
            await FailAsync(runId, stepRun, target.Error, ct).ConfigureAwait(false);
            return;
        }

        var release = target.Release!;
        var commit = release.CommitHash!;
        var result = await branchAdvance
            .TryFastForwardAsync(target.ProjectId, target.Branch, commit, ct)
            .ConfigureAwait(false);
        switch (result.Outcome)
        {
            case GitBranchAdvanceOutcome.Advanced:
                // Audited: the only path that moves a branch without anyone pushing to it, so "who
                // moved main and why" must be answerable from the audit log alone.
                await audit.LogAsync(
                    "AdvancedBranchAfterDeploy", "Release", release.Id,
                    $"{target.Branch} -> {commit} in {result.RepositorySlug} (run {runId}, was {result.PreviousSha ?? "absent"})",
                    ct).ConfigureAwait(false);
                Succeed(stepRun, $"'{target.Branch}' advanced from {Short(result.PreviousSha) ?? "(new branch)"} to {Short(commit)} "
                    + $"(release {release.Version}, repository {result.RepositorySlug}).");
                return;
            case GitBranchAdvanceOutcome.AlreadyUpToDate:
                Succeed(stepRun, $"'{target.Branch}' already points at {Short(commit)} (release {release.Version}); nothing to advance.");
                return;
            case GitBranchAdvanceOutcome.NotFastForward:
                await FailAsync(runId, stepRun,
                    $"'{target.Branch}' is at {Short(result.PreviousSha)}, which is not an ancestor of {Short(commit)} "
                    + $"(release {release.Version}): a fast-forward is impossible, the branch was left where it is.",
                    ct).ConfigureAwait(false);
                return;
            case GitBranchAdvanceOutcome.CommitNotFound:
                await FailAsync(runId, stepRun,
                    $"no internal repository of the project holds commit {commit} of release {release.Version}.",
                    ct).ConfigureAwait(false);
                return;
            default:
                await FailAsync(runId, stepRun,
                    $"the advance of '{target.Branch}' to {Short(commit)} was refused: {result.Detail ?? "no detail"}.",
                    ct).ConfigureAwait(false);
                return;
        }
    }

    private async Task<AdvanceTarget> ResolveTargetAsync(
        int runId, PipelineStepDefinition stepDef, Dictionary<string, string> legVars, CancellationToken ct)
    {
        var branch = SubstituteVariables(stepDef.Branch ?? string.Empty, legVars).Trim();
        if (branch.Length == 0)
            return AdvanceTarget.Failed("missing 'branch' to advance.");

        var version = SubstituteVariables($"$({CandidateVersionVariable})", legVars).Trim();
        if (version.Length == 0 || version.Contains("$(", StringComparison.Ordinal))
            return AdvanceTarget.Failed(
                $"the run sets no {CandidateVersionVariable}: the branch moves onto the commit of the release this run deploys, and the run names none.");

        var run = await pipelines.GetPipelineRunWithPipelineAsync(runId, ct).ConfigureAwait(false);
        var projectId = run?.Pipeline is null
            ? null
            : await pipelines.GetPipelineProjectIdAsync(run.Pipeline, ct).ConfigureAwait(false);
        if (projectId is not { } project)
            return AdvanceTarget.Failed("advance-branch is only supported for project-owned pipelines.");

        var release = await pipelines.FindBranchAdvanceReleaseAsync(project, version, ct).ConfigureAwait(false);
        if (release is null)
            return AdvanceTarget.Failed($"release '{version}' does not exist in this project.");
        if (release.Status != ReleaseStatus.Deployed)
            return AdvanceTarget.Failed(
                $"release '{version}' is {release.Status}, not Deployed: place this step after the type: release step with deployed: true.");
        if (string.IsNullOrWhiteSpace(release.CommitHash))
            return AdvanceTarget.Failed($"release '{version}' records no commit, so there is nothing to advance onto.");
        return new AdvanceTarget(project, branch, release, null);
    }

    private void Succeed(PipelineStepRun stepRun, string message)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        stepRun.Status = TaskExecutionStatus.Success;
        stepRun.ExitCode = 0;
        stepRun.StartedAt ??= now;
        stepRun.CompletedAt = now;
        // Recette R2-071: an advance that worked is the expected outcome of a delivery, not a warning.
        // It was appended to the run's warnings and shown in the yellow banner, which read as an error.
        // It stays on the step, as its output, and in the audit log.
        stepRun.OutputVariablesJson = System.Text.Json.JsonSerializer.Serialize(
            new Dictionary<string, string> { [OutcomeOutput] = message });
    }

    /// <summary>The step output that carries what the advance did.</summary>
    internal const string OutcomeOutput = "BRANCH_ADVANCE";

    private async Task FailAsync(int runId, PipelineStepRun stepRun, string reason, CancellationToken ct)
    {
        MarkSystemStepFailed(stepRun, TaskFailureCodes.ToolError, reason, timeProvider.GetUtcNow().UtcDateTime);
        await pipelines.AppendRunWarningsAsync(
            runId, [$"Advance branch step '{stepRun.StepName}': {reason}"], ct).ConfigureAwait(false);
    }

    private static string? Short(string? sha) => sha is { Length: > 12 } ? sha[..12] : sha;

    private sealed record AdvanceTarget(int ProjectId, string Branch, BranchAdvanceRelease? Release, string? Error)
    {
        public static AdvanceTarget Failed(string error) => new(0, string.Empty, null, error);
    }
}
