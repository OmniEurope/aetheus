// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Pipelines.Events;
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Services.DomainEvents;
using YamlDotNet.Core;

namespace Aetheus.Back.Components.Pipelines;

/// <summary>
/// Pipeline chaining: when an upstream run SUCCEEDS, trigger each pipeline declared under the upstream
/// YAML's <c>on_success:</c> list, forwarding the upstream context (and optional release selector) as
/// variables so a downstream <c>type: deploy</c> step can deploy the produced release. Resolution is
/// project-scoped; a target that doesn't resolve is logged and skipped (never fails the upstream run).
/// </summary>
public sealed class PipelineRunCompletedDownstreamHandler(
    IPipelineRepository repo,
    IPipelineRunService runService,
    ILogger<PipelineRunCompletedDownstreamHandler> logger)
    : IDomainEventHandler<PipelineRunCompletedEvent>
{
    public async Task HandleAsync(PipelineRunCompletedEvent domainEvent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        if (domainEvent.Status != PipelineStatus.Success) return;

        var run = await repo.GetPipelineRunWithPipelineAsync(domainEvent.PipelineRunId, ct).ConfigureAwait(false);
        if (run?.Pipeline is null) return;
        var def = ParseDefinition(run, domainEvent.PipelineRunId);
        if (def?.OnSuccess is not { Count: > 0 }) return;

        var projectId = await repo.GetPipelineProjectIdAsync(run.Pipeline, ct).ConfigureAwait(false);
        if (projectId is not { } pid) return; // downstream resolution is project-scoped

        // The ancestor chain travels in UPSTREAM_CHAIN (comma-separated pipeline ids). Append this run's
        // pipeline so the next hop can detect A→B→A cycles and a runaway depth - not just immediate A→A.
        var ancestorChain = PipelineUpstreamChain.Read(run.AdditionalVariablesJson);
        var chain = new List<int>(ancestorChain) { run.PipelineId };

        foreach (var trigger in def.OnSuccess)
            await TriggerDownstreamAsync(trigger, run, pid, chain, ct).ConfigureAwait(false);
    }

    private PipelineYamlDefinition? ParseDefinition(PipelineRun run, int runId)
    {
        var yaml = !string.IsNullOrWhiteSpace(run.YamlSnapshot) ? run.YamlSnapshot : run.Pipeline!.YamlDefinition;
        try
        {
            return YamlParsingHelper.Deserializer.Deserialize<PipelineYamlDefinition>(yaml);
        }
        catch (YamlException ex)
        {
            logger.LogWarning(ex, "Downstream chaining skipped for run {RunId}: run YAML snapshot failed to parse", runId);
            return null;
        }
    }

    private async Task TriggerDownstreamAsync(
        PipelineDownstreamTrigger trigger, PipelineRun run, int projectId,
        IReadOnlyCollection<int> chain, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(trigger.Pipeline)) return;
        var target = await repo.FindPipelineByNameAndProjectAsync(
            trigger.Pipeline.Trim(), projectId, ct).ConfigureAwait(false);
        if (target is null)
        {
            logger.LogWarning("Downstream trigger: pipeline '{Name}' not found in project {ProjectId}", trigger.Pipeline, projectId);
            return;
        }
        if (chain.Contains(target.Id))
        {
            logger.LogWarning("Downstream trigger skipped: pipeline {Id} already in the upstream chain [{Chain}] - cycle refused", target.Id, string.Join(",", chain));
            return;
        }
        if (chain.Count >= MaxChainDepth)
        {
            logger.LogWarning("Downstream trigger skipped: upstream chain depth {Depth} reached the limit {Max}", chain.Count, MaxChainDepth);
            return;
        }

        var vars = BuildDownstreamVariables(trigger, run, chain);
        var child = await runService.TriggerChainedRunAsync(target.Id, vars, ct).ConfigureAwait(false);
        if (child is null)
        {
            logger.LogWarning("Downstream pipeline {TargetId} not triggered after run {RunId}: owner lacks authority on its target servers", target.Id, run.Id);
            return;
        }
        logger.LogInformation("Downstream pipeline {TargetId} triggered after run {RunId} succeeded", target.Id, run.Id);
    }

    private static Dictionary<string, string> BuildDownstreamVariables(
        PipelineDownstreamTrigger trigger, PipelineRun run, IReadOnlyCollection<int> chain)
    {
        var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["UPSTREAM_RUN_ID"] = run.Id.ToString(),
            ["UPSTREAM_PIPELINE"] = run.Pipeline!.Name,
            ["UPSTREAM_CHAIN"] = string.Join(",", chain)
        };
        if (!string.IsNullOrWhiteSpace(trigger.Release)) vars["UPSTREAM_RELEASE"] = trigger.Release.Trim();
        if (run.CommitHash is { Length: 40 or 64 } && run.CommitHash.All(Uri.IsHexDigit)) vars["AETHEUS_SOURCE_COMMIT"] = run.CommitHash;
        if (!string.IsNullOrWhiteSpace(run.BranchName)) vars["AETHEUS_RUN_BRANCH"] = run.BranchName;
        return vars;
    }

    // Hard cap on chained-pipeline depth - a backstop even when no node repeats (e.g. A→B→C→…→Z).
    private const int MaxChainDepth = 10;

}
