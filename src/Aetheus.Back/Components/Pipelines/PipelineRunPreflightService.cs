// SPDX-License-Identifier: EUPL-1.2
using System.Globalization;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Helpers;

namespace Aetheus.Back.Components.Pipelines;

/// <summary>
/// Answers "can this run reach its end at all?" before a single task is dispatched.
///
/// The stage-level gates (runner resolution, environment checks P-22) are evaluated when each stage
/// is dispatched, which is correct - the fleet changes while a run is in flight. But it means a
/// qualification chain can spend an hour in CI, Quality and Security before discovering that the QA
/// stage names an environment whose required check refuses, or a pool nobody is configured for.
/// This runs the same checks up front and refuses the launch instead.
///
/// It never duplicates the decision, only anticipates it: the dispatch-time gates stay exactly as
/// they are, and this refuses only on conditions that cannot resolve themselves, never on a runner
/// that is merely offline right now (that case is the scheduler's stand-by path, not an error).
/// </summary>
public interface IPipelineRunPreflightService
{
    /// <summary>Empty when the run may start; otherwise one readable reason per problem found.</summary>
    Task<IReadOnlyList<string>> FindBlockingProblemsAsync(
        PipelineYamlDefinition definition,
        IReadOnlyDictionary<string, string> resolvedVariables,
        int? organizationId,
        int? projectId,
        CancellationToken ct);

    /// <summary>
    /// The ADVISORY preflight behind the launch dialog: per stage, which runner would take it and why
    /// not, plus the resolution warnings. It never refuses anything - that is
    /// <see cref="FindBlockingProblemsAsync"/>'s job - it only shows the user what a launch would do.
    ///
    /// It lives here rather than on the facade (A360-36) because both preflights answer the same
    /// question against the same resolver, and keeping them apart meant the facade carried 46 lines of
    /// resolution logic that this collaborator already existed to hold.
    /// </summary>
    Task<PipelinePreflightDto> BuildAdvisoryPreflightAsync(
        PipelineYamlDefinition definition,
        IReadOnlyDictionary<string, string> resolvedVariables,
        List<string> warnings,
        int? organizationId,
        CancellationToken ct);

    /// <summary>
    /// Refuses two deploy steps that would land the same app on the same target. Two stages resolving
    /// to one runner make the second deployment overwrite the first, which reads as a mysterious
    /// rollback rather than a configuration error.
    /// </summary>
    void ValidateUniqueDeploymentTargets(
        PipelineYamlDefinition definition,
        IReadOnlyDictionary<string, string> variables);
}

public sealed class PipelineRunPreflightService(
    IPipelineRepository repo,
    IPipelineDispatchServerResolver dispatchServers,
    IPipelineEnvironmentCheckGuard environmentChecks,
    ILogger<PipelineRunPreflightService> logger) : IPipelineRunPreflightService
{
    public async Task<IReadOnlyList<string>> FindBlockingProblemsAsync(
        PipelineYamlDefinition definition,
        IReadOnlyDictionary<string, string> resolvedVariables,
        int? organizationId,
        int? projectId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(resolvedVariables);

        var problems = new List<string>();
        var stages = YamlParsingHelper.FlattenJobs(definition).ToList();

        // Each of the three checks below is deduplicated on its own key, because a launch is on the
        // synchronous request path and every one of them was previously repeated per stage: the fleet
        // query per selector, an outbound DNS+HTTP call per environment check, and one database round
        // trip per referenced pipeline NAME. A ten-stage pipeline paid all three ten times over.
        var candidateCache = new Dictionary<string, IReadOnlyCollection<int>>(StringComparer.Ordinal);
        foreach (var stage in stages)
            await CheckRunnerIsConfiguredAsync(
                stage, resolvedVariables, organizationId, problems, candidateCache, ct).ConfigureAwait(false);

        foreach (var environmentGroup in stages
                     .Where(stage => !string.IsNullOrEmpty(stage.Environment))
                     .GroupBy(stage => stage.Environment!, StringComparer.OrdinalIgnoreCase))
            await CheckEnvironmentAdmitsTheRunAsync(environmentGroup, problems, ct).ConfigureAwait(false);

        if (projectId is { } id)
            await CheckReferencedPipelinesExistAsync(stages, id, problems, ct).ConfigureAwait(false);

        if (problems.Count > 0)
            logger.LogWarning(
                "Preflight refused a launch with {ProblemCount} unmet requirement(s): {Problems}",
                problems.Count,
                string.Join(" | ", problems));

        return problems;
    }

    public async Task<PipelinePreflightDto> BuildAdvisoryPreflightAsync(
        PipelineYamlDefinition definition,
        IReadOnlyDictionary<string, string> resolvedVariables,
        List<string> warnings,
        int? organizationId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(resolvedVariables);

        var stages = new List<PreflightStageDto>();
        foreach (var stage in YamlParsingHelper.FlattenJobs(definition))
        {
            var server = await dispatchServers.ResolveServerForTargetAsync(
                stage, resolvedVariables, organizationId, dispatchServers.StageHasDeployStep(stage), ct)
                .ConfigureAwait(false);
            var (kind, label) = PipelineRunHelpers.DescribeTarget(stage);
            stages.Add(new PreflightStageDto
            {
                StageName = stage.Name,
                TargetKind = kind,
                Target = label,
                Resolved = server is not null,
                ServerName = server?.Name,
                Reason = server is null ? dispatchServers.BuildNoServerReason(stage.Name, stage) : null
            });
        }

        return new PipelinePreflightDto { Stages = stages, Warnings = warnings };
    }

    public void ValidateUniqueDeploymentTargets(
        PipelineYamlDefinition definition,
        IReadOnlyDictionary<string, string> variables)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(variables);
        // SubstituteVariables takes a concrete Dictionary; the callers hold one, but the interface is
        // read-only so the contract cannot be widened by accident.
        var substitutions = variables as Dictionary<string, string>
            ?? new Dictionary<string, string>(variables, StringComparer.OrdinalIgnoreCase);
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var stage in YamlParsingHelper.FlattenJobs(definition))
        {
            var target = !string.IsNullOrWhiteSpace(stage.Environment)
                ? $"environment:{PipelineRunHelpers.SubstituteVariables(stage.Environment, substitutions)}"
                : !string.IsNullOrWhiteSpace(stage.Pool)
                    ? $"pool:{PipelineRunHelpers.SubstituteVariables(stage.Pool, substitutions)}"
                    : $"agent:{PipelineRunHelpers.SubstituteVariables(stage.Agent ?? string.Empty, substitutions)}";
            foreach (var step in stage.Steps.Where(item =>
                         string.Equals(item.Type, "deploy", StringComparison.OrdinalIgnoreCase)))
            {
                var app = PipelineRunHelpers.SubstituteVariables(step.App ?? string.Empty, substitutions).Trim();
                if (app.Length == 0) continue;
                var key = $"{target}|app:{app}";
                var path = $"stage '{stage.Name}' / step '{step.Name}'";
                if (seen.TryGetValue(key, out var first))
                    throw new BadRequestException(
                        $"Duplicate deployment target '{app}' on {target}: {first} and {path}. Override the inherited step by using the same name or remove one deployment.");
                seen[key] = path;
            }
        }
    }

    /// <summary>
    /// Refuses only when NO server is configured for the stage's selectors. A configured server that
    /// is currently offline is not a preflight failure: the planner already parks the run and picks
    /// it up when the runner comes back, and turning that into a launch error would make a transient
    /// outage look like a broken pipeline.
    /// </summary>
    private async Task CheckRunnerIsConfiguredAsync(
        PipelineStageDefinition stage,
        IReadOnlyDictionary<string, string> resolvedVariables,
        int? organizationId,
        List<string> problems,
        Dictionary<string, IReadOnlyCollection<int>> candidateCache,
        CancellationToken ct)
    {
        var effective = dispatchServers.ResolveEffectiveStageTarget(stage, resolvedVariables);
        var deploymentStage = dispatchServers.StageHasDeployStep(stage);
        // Stages of one pipeline overwhelmingly share a selector, and the answer cannot change during a
        // single preflight, so the fleet is queried once per DISTINCT selector rather than once per stage.
        var cacheKey = string.Join(
            "|",
            effective.Pool, effective.Environment, effective.Agent, effective.Os,
            organizationId?.ToString(CultureInfo.InvariantCulture), deploymentStage);
        if (!candidateCache.TryGetValue(cacheKey, out var configured))
        {
            configured = await repo.FindCandidateTargetServerIdsAsync(
                effective.Pool, effective.Environment, effective.Agent,
                OsTypeHelper.Parse(effective.Os), organizationId, deploymentStage, ct).ConfigureAwait(false);
            candidateCache[cacheKey] = configured;
        }

        if (configured.Count > 0) return;

        // A stage with no selector at all falls back to any runner in the organization; the candidate
        // query above cannot express that, so it is left to dispatch rather than refused here.
        var hasSelector = !string.IsNullOrEmpty(effective.Pool)
                          || !string.IsNullOrEmpty(effective.Environment)
                          || !string.IsNullOrEmpty(effective.Agent)
                             && !effective.Agent.Equals("default", StringComparison.OrdinalIgnoreCase);
        if (!hasSelector && !deploymentStage) return;

        problems.Add(deploymentStage
            ? dispatchServers.BuildNoDeployTargetReason(stage.Name, stage)
            : dispatchServers.BuildNoServerReason(stage.Name, stage));
    }

    /// <summary>
    /// Evaluated once per DISTINCT environment, not once per stage. The guard reloads the environment
    /// and issues a DNS lookup plus an HTTP GET for every required check, each with its own timeout
    /// budget, so five stages targeting one environment with two 30 s checks could add minutes to the
    /// launch request. The environment's verdict is the same for every stage that names it.
    /// </summary>
    private async Task CheckEnvironmentAdmitsTheRunAsync(
        IGrouping<string, PipelineStageDefinition> environmentStages,
        List<string> problems,
        CancellationToken ct)
    {
        // Same guard the dispatcher uses, so the answer here and an hour later are the same answer.
        if (await environmentChecks.CheckEnvironmentChecksAsync(environmentStages.First(), ct)
                .ConfigureAwait(false))
            return;

        var stageNames = string.Join("', '", environmentStages.Select(stage => stage.Name));
        problems.Add(
            $"Stage '{stageNames}': a required check on environment '{environmentStages.Key}' refuses "
            + "the run. Fix the check before launching; it is evaluated again at dispatch.");
    }

    /// <summary>
    /// A step that names another pipeline (a <c>trigger</c>, or an artifact restored from another
    /// pipeline) fails at its own stage, which in a qualification chain is hours in. The name is
    /// known at launch, so a typo or a deleted pipeline is caught at launch.
    /// </summary>
    private async Task CheckReferencedPipelinesExistAsync(
        IReadOnlyList<PipelineStageDefinition> stages,
        int projectId,
        List<string> problems,
        CancellationToken ct)
    {
        // Existence is a property of the NAME, not of the stage that references it. An orchestrator
        // naming four pipelines across thirty steps used to issue thirty queries for four answers, so
        // each distinct name is resolved once and every referencing site reuses the verdict.
        var references = stages
            .SelectMany(stage => stage.Steps.SelectMany(
                step => ReferencedPipelines(step).Select(reference => (StageName: stage.Name, PipelineName: reference.Name, reference.What))))
            .ToList();

        var verdicts = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in references.Select(reference => reference.PipelineName).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var target = await repo.FindPipelineByNameAndProjectAsync(name, projectId, ct).ConfigureAwait(false);
            verdicts[name] = target is not null;
        }

        foreach (var (stageName, name, what) in references)
            if (!verdicts[name])
                problems.Add(
                    $"Stage '{stageName}': {what} names pipeline '{name}', which does not exist in this project.");
    }

    private static IEnumerable<(string Name, string What)> ReferencedPipelines(PipelineStepDefinition step)
    {
        if (!string.IsNullOrWhiteSpace(step.Pipeline))
            yield return (step.Pipeline.Trim(), $"step '{step.Name}'");
        if (!string.IsNullOrWhiteSpace(step.ArtifactSourcePipeline))
            yield return (step.ArtifactSourcePipeline.Trim(), $"step '{step.Name}' (artifact source)");
    }
}
