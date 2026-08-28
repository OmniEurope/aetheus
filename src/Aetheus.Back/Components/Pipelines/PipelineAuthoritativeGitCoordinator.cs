// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Pipelines;

internal static class PipelineAuthoritativeGitCoordinator
{
    public static async Task CommitCreateAsync(
        Pipeline pipeline, IPipelineGitService pipelineGit, ILogger logger,
        string actor, CancellationToken ct)
    {
        if (pipeline.ProjectId is not { } projectId) return;
        var (outcome, error) = await pipelineGit.WriteProjectPipelineYamlAsync(
            projectId, pipeline.Name, pipeline.YamlDefinition, actor, ct,
            pipeline.SourceBranch, pipeline.SourceRepositoryId)
            .ConfigureAwait(false);
        if (outcome == GitWriteOutcome.NoRepo)
            logger.LogInformation(
                "Pipeline '{Name}' has no internal git repo; DB definition is authoritative.", pipeline.Name);
        else if (outcome == GitWriteOutcome.Failed)
            throw new ConflictException(
                $"Pipeline definition could not be committed to the project's git repository: {error}");
    }

    public static async Task CopyToLinkedEnvironmentProjectAsync(
        Pipeline pipeline, IPipelineRepository repo, IPipelineGitService pipelineGit,
        ILogger logger, string actor, CancellationToken ct)
    {
        if (pipeline.EnvironmentId is not { } environmentId) return;
        var target = await repo.GetEnvironmentCopyTargetAsync(environmentId, ct).ConfigureAwait(false);
        if (target is not { } copyTarget) return;
        try
        {
            await pipelineGit.CopyEnvironmentPipelinesToProjectAsync(
                environmentId, copyTarget.Name, copyTarget.ProjectId, actor, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Env-linked pipeline copy failed for environment {EnvId} → project {ProjectId}.",
                environmentId, copyTarget.ProjectId);
        }
    }
}
