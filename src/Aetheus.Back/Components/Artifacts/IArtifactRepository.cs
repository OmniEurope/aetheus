// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Artifacts;

public interface IArtifactRepository
{
    Task<PipelineArtifact?> FindAsync(int id, CancellationToken ct = default);
    Task<(List<PipelineArtifact> Items, int TotalCount)> GetByProjectPagedAsync(int projectId, ArtifactRetentionPolicy? policy, int? pipelineId, int page, int pageSize, CancellationToken ct = default);
    Task<List<PipelineArtifact>> GetByPipelineAndProjectAsync(int pipelineId, int projectId, ArtifactRetentionPolicy policy, CancellationToken ct = default);
    Task<List<PipelineArtifact>> GetByRunAsync(int pipelineRunId, CancellationToken ct = default);
    Task<List<PipelineArtifact>> GetByEnvironmentAsync(int pipelineId, int projectId, string environmentName, CancellationToken ct = default);
    Task<List<PipelineArtifact>> GetReleasesAsync(int pipelineId, int projectId, CancellationToken ct = default);
    Task<List<PipelineArtifact>> GetExpiredAsync(DateTime cutoff, int batchSize, CancellationToken ct = default);
    Task<bool> HasActiveRetentionLeaseAsync(int artifactId, DateTime at, CancellationToken ct = default);
    Task<List<PipelineArtifact>> GetProjectBuildArtifactsAsync(int projectId, CancellationToken ct = default);
    Task AddAsync(PipelineArtifact artifact, CancellationToken ct = default);
    Task LinkReleaseAsync(PipelineArtifact artifact, int releaseId, CancellationToken ct = default);
    Task RemoveAsync(PipelineArtifact artifact, CancellationToken ct = default);
    Task<int> GetNextBuildNumberAsync(int projectId, int pipelineId, CancellationToken ct = default);
    // Aggregate on-record size of all artifacts of a project (for the storage quota guard).
    Task<long> GetProjectTotalSizeBytesAsync(int projectId, CancellationToken ct = default);
    // S-FEAT-15: per-project retention overrides (days); each null when the project uses the global default.
    Task<(int? DefaultDays, int? LatestDays)> GetProjectRetentionOverridesAsync(int projectId, CancellationToken ct = default);

    // Cross-agent deploy resolution. Same-run: the newest artifact named <name> produced by <runId>
    // (scenario 1). Release: the artifact of an existing release selected by id / version / "latest"
    // (scenario 3). The release selector also accepts "latest-published", which ignores drafts and
    // failed releases. Rollback compatibility gates use the dedicated previous-published lookup so a
    // release of the commit currently under test can never be mistaken for V-1. All return null when
    // nothing matches, so the deploy/restore step fails honestly.
    Task<PipelineArtifact?> FindRunArtifactByNameAsync(int runId, string name, CancellationToken ct = default);
    Task<PipelineArtifact?> FindSuccessfulPipelineArtifactByCommitAsync(
        int projectId, string pipelineName, string commitHash, string name, CancellationToken ct = default);
    Task<PipelineArtifact?> FindReleaseArtifactAsync(
        int projectId, string releaseSelector, string? artifactName = null, CancellationToken ct = default);
    Task<ReleaseArtifactSelection?> FindReleaseArtifactSelectionAsync(
        int projectId, string releaseSelector, string? artifactName = null, CancellationToken ct = default);
    Task<bool> HasDeployedRollbackContractReleaseAsync(int projectId, CancellationToken ct = default);
    Task<PipelineArtifact?> FindPreviousPublishedReleaseArtifactAsync(
        int projectId, string currentCommitHash, string? artifactName = null, CancellationToken ct = default);
    Task<PipelineArtifact?> FindPreviousDeployedReleaseArtifactAsync(
        int projectId, string currentCommitHash, string? artifactName = null, CancellationToken ct = default);
    Task<bool> HasPublishedRollbackContractReleaseAsync(int projectId, CancellationToken ct = default);
    Task<bool> RequiresPreviousPublishedArtifactAsync(
        int projectId, string currentCommitHash, string? artifactName, CancellationToken ct = default);
    Task<bool> RequiresPreviousDeployedArtifactAsync(
        int projectId, string currentCommitHash, string? artifactName, CancellationToken ct = default);
    // IDOR-safe deploy download: the agent's server org must equal the artifact's project org. Null
    // when the server is unknown.
    Task<int?> GetServerOrganizationIdAsync(int serverId, CancellationToken ct = default);

    // Own-reads over entities Artifacts shares with Pipelines and Releases. Obtaining them by
    // injecting those modules' services is what put Artifacts inside a module cycle, for reads that
    // change no ownership: the writes stay where they were.
    Task<(int PipelineId, int? ProjectId)?> GetRunPipelineContextAsync(int runId, CancellationToken ct = default);
    Task<bool> IsServerAssignedToRunAsync(int runId, int serverId, CancellationToken ct = default);
    Task<Release?> FindReleaseForRunAsync(int pipelineRunId, CancellationToken ct = default);
    Task<List<Release>> GetDeployedProjectReleasesAsync(int projectId, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}

public sealed record ReleaseArtifactSelection(PipelineArtifact Artifact, int ReleaseId);
