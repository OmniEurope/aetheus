// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.GitGraph;
using Aetheus.Shared.Helpers;
using static Aetheus.Back.Components.Pipelines.PipelineDtoMapper;
using static Aetheus.Back.Components.Pipelines.PipelineRunHelpers;

namespace Aetheus.Back.Components.Pipelines;

/// <summary>Reads one run back as the DTO the API and the launcher both return.</summary>
public interface IPipelineRunReader
{
    /// <summary>The run with its steps, queue positions and mapped detail, or <c>null</c> if unknown.</summary>
    Task<PipelineRunDto?> GetRunAsync(int runId, CancellationToken ct = default);
}

/// <summary>
/// The run-detail query, extracted from <see cref="PipelineRunService"/> so that
/// <see cref="PipelineRunLauncher"/> can return a launched run without depending on the facade it is
/// called from. A leaf: repository read plus mapping, no state change.
/// </summary>
public sealed class PipelineRunReader(
    IPipelineRepository repo,
    IGitGraphRecorder gitGraph,
    ISecretMaskingService secretMasking) : IPipelineRunReader
{
    public async Task<PipelineRunDto?> GetRunAsync(int runId, CancellationToken ct = default)
    {
        var run = await repo.GetRunDetailAsync(runId, ct).ConfigureAwait(false);
        if (run is null) return null;

        var dto = MapRunToDto(run);
        var queuedTaskIds = dto.Steps
            .Where(step => step.Status == TaskExecutionStatus.Assigned && step.TaskId.HasValue)
            .Select(step => step.TaskId!.Value)
            .ToArray();
        if (queuedTaskIds.Length > 0)
        {
            var positions = await repo.GetTaskQueuePositionsAsync(queuedTaskIds, ct).ConfigureAwait(false);
            dto = dto with
            {
                Steps = dto.Steps.Select(step =>
                    step.TaskId is { } taskId && positions.TryGetValue(taskId, out var queue)
                        ? step with { QueuePosition = queue.Position, QueueDepth = queue.Depth }
                        : step).ToList()
            };
        }
        // Resolve the run's commit/branch to internal git-graph pages (mirrors the Release/Artifact
        // detail views). Cheap point lookups; empty when no graph node exists yet (plain-text fallback).
        if (run.Pipeline?.ProjectId is { } projectId)
        {
            var (commits, branches) = await gitGraph.ResolveRunLinksAsync(projectId, run.CommitHash, run.BranchName, ct).ConfigureAwait(false);
            dto = dto with { Commits = commits, Branches = branches };
        }

        // S-UX-18: surface each step's executed command, but mask substituted secret values first -
        // ServerTask.Command stores the resolved command verbatim (unlike logs, masked at write time).
        // OutputVariables get the same treatment: a step can `setvariable` a secret's value, which
        // would otherwise reach any Project.Read user verbatim, bypassing the log masking.
        if (dto.Steps.Any(s => !string.IsNullOrEmpty(s.Command)
                               || !string.IsNullOrEmpty(s.FailureReason)
                               || s.OutputVariables.Count > 0))
        {
            var maskedSteps = new List<PipelineStepRunDto>(dto.Steps.Count);
            foreach (var step in dto.Steps)
            {
                var masked = step;
                if (!string.IsNullOrEmpty(step.Command))
                    masked = masked with { Command = await secretMasking.MaskAsync(step.Command, runId, ct).ConfigureAwait(false) };
                if (!string.IsNullOrEmpty(step.FailureReason))
                    masked = masked with
                    {
                        FailureReason = await secretMasking.MaskAsync(step.FailureReason, runId, ct).ConfigureAwait(false)
                    };
                if (step.OutputVariables.Count > 0)
                {
                    var maskedVars = new Dictionary<string, string>(step.OutputVariables.Count);
                    foreach (var (key, value) in step.OutputVariables)
                        maskedVars[key] = await secretMasking.MaskAsync(value, runId, ct).ConfigureAwait(false);
                    masked = masked with { OutputVariables = maskedVars };
                }
                maskedSteps.Add(masked);
            }
            dto = dto with { Steps = maskedSteps };
        }

        // Trigger steps are NOT flattened into this run's step list. Each keeps its TriggeredRunId +
        // TriggeredPipelineName so the run view renders the child pipeline as a collapsible node and
        // lazy-loads its own run detail on expand (recursive tree, keyed by run id - see the front
        // PipelineRun timeline). Flattening here produced an unreadable, duplicate-prone linear list.
        return dto;
    }
}
