// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.Enums;

namespace Aetheus.Back.Components.Artifacts;

public sealed class ArtifactRetentionService(
    IArtifactRepository repo,
    IConfiguration configuration,
    TimeProvider timeProvider) : IArtifactRetentionService
{
    private int DefaultRetentionDays => configuration.GetValue("ArtifactStorage:DefaultRetentionDays", 1);
    private int LatestRetentionDays => configuration.GetValue("ArtifactStorage:LatestRetentionDays", 30);

    // S-FEAT-15: per-project overrides take precedence over the global config defaults.
    private async Task<(int Default, int Latest)> ResolveRetentionAsync(int projectId, CancellationToken ct)
    {
        var (overrideDefault, overrideLatest) = await repo.GetProjectRetentionOverridesAsync(projectId, ct).ConfigureAwait(false);
        return (overrideDefault ?? DefaultRetentionDays, overrideLatest ?? LatestRetentionDays);
    }

    public async Task ApplyBuildRetentionAsync(PipelineArtifact newArtifact, CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var (defaultDays, latestDays) = await ResolveRetentionAsync(newArtifact.ProjectId!.Value, ct).ConfigureAwait(false);

        newArtifact.RetentionPolicy = ArtifactRetentionPolicy.Build;
        newArtifact.RetentionExpiresAt = now.AddDays(latestDays);

        var previousBuilds = await repo.GetByPipelineAndProjectAsync(
            newArtifact.PipelineId, newArtifact.ProjectId!.Value, ArtifactRetentionPolicy.Build, ct).ConfigureAwait(false);

        var previousDeadline = now.AddDays(defaultDays);
        foreach (var prev in previousBuilds.Where(a => a.Id != newArtifact.Id))
            ShortenRetention(prev, previousDeadline);

        await repo.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task ApplyDeployRetentionAsync(PipelineArtifact artifact, string environmentName, CancellationToken ct = default)
    {
        // A project-less artifact (orphan run) has no per-project retention to resolve and no
        // environment cohort to age out - flag it deployed with the global-latest window and stop,
        // rather than dereferencing a null ProjectId (the prod NRE class this guard closes).
        if (artifact.ProjectId is not { } projectId)
        {
            artifact.RetentionPolicy = ArtifactRetentionPolicy.Deployed;
            artifact.EnvironmentName = environmentName;
            artifact.RetentionExpiresAt = timeProvider.GetUtcNow().UtcDateTime.AddDays(LatestRetentionDays);
            await repo.SaveChangesAsync(ct).ConfigureAwait(false);
            return;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var (defaultDays, latestDays) = await ResolveRetentionAsync(projectId, ct).ConfigureAwait(false);

        artifact.RetentionPolicy = ArtifactRetentionPolicy.Deployed;
        artifact.EnvironmentName = environmentName;
        artifact.RetentionExpiresAt = now.AddDays(latestDays);

        var previousDeployed = await repo.GetByEnvironmentAsync(
            artifact.PipelineId, projectId, environmentName, ct).ConfigureAwait(false);

        var previousDeadline = now.AddDays(defaultDays);
        foreach (var prev in previousDeployed.Where(a => a.Id != artifact.Id))
            ShortenRetention(prev, previousDeadline);

        await repo.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task ApplyReleaseRetentionAsync(PipelineArtifact artifact, int releaseId, CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var (defaultDays, latestDays) = await ResolveRetentionAsync(artifact.ProjectId!.Value, ct).ConfigureAwait(false);

        artifact.RetentionPolicy = ArtifactRetentionPolicy.Released;
        await repo.LinkReleaseAsync(artifact, releaseId, ct).ConfigureAwait(false);
        artifact.RetentionExpiresAt = now.AddDays(latestDays);

        var previousReleases = await repo.GetReleasesAsync(
            artifact.PipelineId, artifact.ProjectId!.Value, ct).ConfigureAwait(false);

        var previousDeadline = now.AddDays(defaultDays);
        foreach (var prev in previousReleases.Where(a => a.Id != artifact.Id))
            ShortenRetention(prev, previousDeadline);

        await repo.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static void ShortenRetention(PipelineArtifact artifact, DateTime deadline)
    {
        // Repeated builds/deployments must not create a sliding retention window. Once an older
        // artifact has been aged out, later runs may shorten that deadline but never extend it.
        if (artifact.RetentionExpiresAt > deadline)
            artifact.RetentionExpiresAt = deadline;
    }
}
