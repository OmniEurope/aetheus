// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Pipelines;

internal static class PipelineArtifactMapper
{
    internal static PipelineArtifactDto ToStandaloneDto(PipelineArtifact artifact) =>
        CreateDto(artifact, artifact.Sha256, null, null, null);

    internal static PipelineArtifactDto ToRunDto(PipelineArtifact artifact, PipelineRun run) =>
        CreateDto(artifact, null, run.BranchName, run.CommitHash, run.RepositoryUrl);

    private static PipelineArtifactDto CreateDto(
        PipelineArtifact artifact,
        string? sha256,
        string? branchName,
        string? commitHash,
        string? repositoryUrl) => new()
    {
        Id = artifact.Id,
        PipelineRunId = artifact.PipelineRunId,
        PipelineId = artifact.PipelineId,
        ProjectId = artifact.ProjectId,
        Name = artifact.Name,
        FilePath = artifact.FilePath,
        SizeBytes = artifact.SizeBytes,
        Sha256 = sha256,
        StageName = artifact.StageName,
        StepName = artifact.StepName,
        CreatedAt = artifact.CreatedAt,
        RetentionPolicy = artifact.RetentionPolicy,
        RetentionExpiresAt = artifact.RetentionExpiresAt,
        EnvironmentName = artifact.EnvironmentName,
        BranchName = branchName,
        CommitHash = commitHash,
        RepositoryUrl = repositoryUrl
    };
}
