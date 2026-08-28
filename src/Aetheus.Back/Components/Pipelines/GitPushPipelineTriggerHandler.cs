// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Git;
using Aetheus.Back.Components.Git.Events;
using Aetheus.Back.Services.DomainEvents;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Helpers;

namespace Aetheus.Back.Components.Pipelines;

/// <summary>
/// What a push means for pipelines: synchronise the definitions it carries, then trigger whatever it
/// should trigger.
///
/// This whole body used to live in <c>GitSmartHttpService</c>, which made the transport layer drive
/// the orchestrator and put Git and Pipelines in the same cycle. Nothing about the behaviour changes
/// here - the ordering (sync definitions first, so a push that edits a pipeline triggers the edited
/// one), the per-pipeline lock, the latest-wins replacement and the swallow-and-log failure handling
/// are all preserved deliberately: a push already written to disk must not be reported as failed
/// because a downstream trigger did not fire.
/// </summary>
internal sealed class GitPushPipelineTriggerHandler(
    IPipelineService pipelineService,
    IPipelineRunService pipelineRunService,
    IPipelineRepository pipelineRepo,
    IGitLightCliService cli,
    ILogger<GitPushPipelineTriggerHandler> logger)
    : IDomainEventHandler<GitPushProcessedEvent>
{
    public async Task HandleAsync(GitPushProcessedEvent domainEvent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        // Definitions first: a push that changes a pipeline's YAML must trigger the changed one.
        foreach (var update in domainEvent.RefUpdates)
            await SyncPipelinesFromRepoAsync(domainEvent, update, ct).ConfigureAwait(false);
        await TriggerPushPipelinesAsync(domainEvent, ct).ConfigureAwait(false);
    }

    private async Task TriggerPushPipelinesAsync(GitPushProcessedEvent push, CancellationToken ct)
    {
        try
        {
            var pipelines = await pipelineService
                .GetWebhookTriggeredPipelinesForProjectAsync(push.ProjectId, ct).ConfigureAwait(false);
            foreach (var pipeline in pipelines)
                await TryTriggerPushPipelineAsync(pipeline, push.RefUpdates, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to trigger pipelines on push for {Slug}", push.Slug);
        }
    }

    private async Task TryTriggerPushPipelineAsync(
        PipelineDto pipeline,
        IReadOnlyList<GitRefUpdate> updatedRefs,
        CancellationToken ct)
    {
        var definition = YamlParsingHelper.ParseAndValidate(pipeline.YamlDefinition, logger);
        var matchingUpdate = definition is null
            ? null
            : updatedRefs.FirstOrDefault(update =>
                PipelineBranchFilter.Matches(definition.Branches, update.Reference, logger));
        if (matchingUpdate is null) return;
        var variables = new Dictionary<string, string>
        {
            ["WEBHOOK_REF"] = matchingUpdate.Reference,
            ["AETHEUS_SOURCE_COMMIT"] = matchingUpdate.NewObjectId
        };
        using var pipelineLock = await PipelineTriggerLocks.AcquireAsync(pipeline.Id, ct).ConfigureAwait(false);
        var hasActiveRun = await pipelineService.HasActiveRunAsync(pipeline.Id, ct).ConfigureAwait(false);
        if (hasActiveRun && definition!.SupersedeRunning != true) return;
        if (!hasActiveRun)
        {
            logger.LogInformation(
                "Git push triggering pipeline {PipelineId} ({PipelineName})", pipeline.Id, pipeline.Name);
            await pipelineRunService.TriggerAutomatedRunAsync(
                pipeline.Id, "GitPush", variables, ct).ConfigureAwait(false);
            return;
        }
        await ReplaceActivePushRunAsync(pipeline, variables, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Latest-wins: prepare and authorize the replacement BEFORE cancelling anything, so a refused
    /// run does not leave the pipeline with nothing running.
    /// </summary>
    private async Task ReplaceActivePushRunAsync(
        PipelineDto pipeline,
        Dictionary<string, string> variables,
        CancellationToken ct)
    {
        var preparation = await pipelineRunService.PrepareAutomatedRunAsync(
            pipeline.Id, "GitPush", variables, ct).ConfigureAwait(false);
        if (preparation is null) return;
        var activeRunIds = await pipelineRepo.GetActiveRunIdsAsync(pipeline.Id, ct).ConfigureAwait(false);
        foreach (var activeRunId in activeRunIds)
            await pipelineRunService.CancelRunAsync(activeRunId, ct).ConfigureAwait(false);
        logger.LogInformation(
            "Git push latest-wins requested cancellation of {Count} superseded run(s) for pipeline {PipelineId}",
            activeRunIds.Count, pipeline.Id);
        var replacement = await pipelineRunService.TriggerPreparedRunAsync(
            preparation, variables, ct: ct).ConfigureAwait(false);
        if (replacement is null)
            logger.LogWarning(
                "Git push replacement for pipeline {PipelineId} could not be persisted", pipeline.Id);
    }

    private async Task SyncPipelinesFromRepoAsync(
        GitPushProcessedEvent push, GitRefUpdate update, CancellationToken ct)
    {
        try
        {
            if (push.ProjectId == 0 || push.DiskPath.Length == 0) return;

            // Carried over verbatim, including its flaw: a non-branch ref (a tag) is sliced at the
            // length of "refs/heads/" anyway and yields a nonsense source branch. Left as-is so this
            // commit stays a move; fixing it here would hide a behaviour change inside a refactor.
            var sourceBranch = update.Reference["refs/heads/".Length..];
            var tree = await cli.GetTreeAsync(push.DiskPath, update.NewObjectId, ".pipeline", ct).ConfigureAwait(false);
            var yamlFiles = tree.Where(entry => entry.Name.EndsWith(".yml", StringComparison.OrdinalIgnoreCase)
                                          || entry.Name.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)).ToList();
            if (yamlFiles.Count == 0) return;

            foreach (var file in yamlFiles)
            {
                var blob = await cli.GetBlobAsync(
                    push.DiskPath, update.NewObjectId, $".pipeline/{file.Name}", ct).ConfigureAwait(false);
                if (blob is null || string.IsNullOrWhiteSpace(blob.Content)) continue;

                var definition = YamlParsingHelper.ParseAndValidate(blob.Content, logger);
                if (definition is null)
                {
                    logger.LogWarning(
                        "Invalid pipeline YAML in .pipeline/{File} for repo {Repo}", file.Name, push.RepositoryName);
                    continue;
                }

                var pipelineName = string.IsNullOrWhiteSpace(definition.Name)
                    ? Path.GetFileNameWithoutExtension(file.Name)
                    : definition.Name;

                await pipelineService.UpsertPipelineFromYamlAsync(
                    pipelineName, blob.Content, push.ProjectId, definition.Trigger, ct,
                    sourceBranch, push.DefaultBranch, push.GitRepoId).ConfigureAwait(false);
                logger.LogInformation(
                    "Synced pipeline '{Name}' from {Commit}:.pipeline/{File}",
                    pipelineName, update.NewObjectId, file.Name);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to sync pipelines from .pipeline/ for repo {Repo}", push.RepositoryName);
        }
    }
}
