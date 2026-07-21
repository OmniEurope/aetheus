// SPDX-License-Identifier: EUPL-1.2
using System.Text.Json;
using Aetheus.Back.Components.Pipelines.Events;
using Aetheus.Back.Services;
using Aetheus.Back.Services.DomainEvents;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
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

        // The run's captured snapshot is the exact YAML that executed; fall back to the live definition.
        var yaml = !string.IsNullOrWhiteSpace(run.YamlSnapshot) ? run.YamlSnapshot : run.Pipeline.YamlDefinition;
        PipelineYamlDefinition? def;
        try
        {
            def = YamlParsingHelper.Deserializer.Deserialize<PipelineYamlDefinition>(yaml);
        }
        catch (YamlException ex)
        {
            // The snapshot already parsed at run creation, so this is unexpected; log it rather than
            // silently dropping any on_success: chaining (a malformed snapshot never blocks completion).
            logger.LogWarning(ex, "Downstream chaining skipped for run {RunId}: run YAML snapshot failed to parse", domainEvent.PipelineRunId);
            return;
        }
        if (def?.OnSuccess is not { Count: > 0 }) return;

        var projectId = await repo.GetPipelineProjectIdAsync(run.Pipeline, ct).ConfigureAwait(false);
        if (projectId is not { } pid) return; // downstream resolution is project-scoped

        // The ancestor chain travels in UPSTREAM_CHAIN (comma-separated pipeline ids). Append this run's
        // pipeline so the next hop can detect A→B→A cycles and a runaway depth - not just immediate A→A.
        var ancestorChain = ReadAncestorChain(run.AdditionalVariablesJson);
        var chain = new List<int>(ancestorChain) { run.PipelineId };

        foreach (var trigger in def.OnSuccess)
        {
            if (string.IsNullOrWhiteSpace(trigger.Pipeline)) continue;

            var target = await repo.FindPipelineByNameAndProjectAsync(trigger.Pipeline.Trim(), pid, ct).ConfigureAwait(false);
            if (target is null)
            {
                logger.LogWarning("Downstream trigger: pipeline '{Name}' not found in project {ProjectId}", trigger.Pipeline, pid);
                continue;
            }

            // Cycle guard: refuse if the target already appears anywhere in the lineage (covers immediate
            // A→A and deeper A→B→A), or if the chain has grown past the safety depth.
            if (chain.Contains(target.Id))
            {
                logger.LogWarning("Downstream trigger skipped: pipeline {Id} already in the upstream chain [{Chain}] - cycle refused", target.Id, string.Join(",", chain));
                continue;
            }
            if (chain.Count >= MaxChainDepth)
            {
                logger.LogWarning("Downstream trigger skipped: upstream chain depth {Depth} reached the limit {Max}", chain.Count, MaxChainDepth);
                continue;
            }

            var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["UPSTREAM_RUN_ID"] = domainEvent.PipelineRunId.ToString(),
                ["UPSTREAM_PIPELINE"] = run.Pipeline.Name,
                ["UPSTREAM_CHAIN"] = string.Join(",", chain)
            };
            if (!string.IsNullOrWhiteSpace(trigger.Release))
                vars["UPSTREAM_RELEASE"] = trigger.Release.Trim();
            if (run.CommitHash is { Length: 40 or 64 } && run.CommitHash.All(Uri.IsHexDigit))
                vars["AETHEUS_SOURCE_COMMIT"] = run.CommitHash;
            if (!string.IsNullOrWhiteSpace(run.BranchName))
                vars["AETHEUS_RUN_BRANCH"] = run.BranchName;

            // F-EXEC-1b (chaining): authorize the downstream pipeline's OWNER against its own target
            // servers before launching - the upstream run's authority does not extend to a child that
            // resolves to a different server set. A blocked hop is audited and skipped, never fakes success.
            var child = await runService.TriggerChainedRunAsync(target.Id, vars, ct).ConfigureAwait(false);
            if (child is null)
            {
                logger.LogWarning("Downstream pipeline {TargetId} not triggered after run {RunId}: owner lacks authority on its target servers", target.Id, domainEvent.PipelineRunId);
                continue;
            }
            logger.LogInformation("Downstream pipeline {TargetId} triggered after run {RunId} succeeded", target.Id, domainEvent.PipelineRunId);
        }
    }

    // Hard cap on chained-pipeline depth - a backstop even when no node repeats (e.g. A→B→C→…→Z).
    private const int MaxChainDepth = 10;

    private static IReadOnlyList<int> ReadAncestorChain(string? additionalVariablesJson)
    {
        if (string.IsNullOrWhiteSpace(additionalVariablesJson)) return [];
        try
        {
            var vars = JsonSerializer.Deserialize<Dictionary<string, string>>(additionalVariablesJson);
            if (vars is null || !vars.TryGetValue("UPSTREAM_CHAIN", out var chainStr) || string.IsNullOrWhiteSpace(chainStr))
                return [];
            return chainStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => int.TryParse(s, out var id) ? id : (int?)null)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
