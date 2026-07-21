// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;

namespace Aetheus.Back.Components.Releases;

public interface IReleaseService
{
    Task<PaginatedResult<ReleaseDto>> GetReleasesAsync(int? projectId, PaginationRequest request, List<int>? accessibleIds = null, CancellationToken ct = default);
    Task<ReleaseDto?> GetReleaseAsync(int id, CancellationToken ct = default);
    Task<ReleaseRollbackPreviewDto> GetRollbackPreviewAsync(int id, CancellationToken ct = default);
    Task<List<ReleaseDto>> GetReleasesByRunAsync(int pipelineRunId, CancellationToken ct = default);
    Task<List<ReleaseDto>> SyncReleasesAsync(int projectId, CancellationToken ct = default);
    Task<ReleaseDto> TriggerReleaseBuildAsync(int releaseId, TriggerReleaseBuildRequest request, CancellationToken ct = default);
    Task<ReleaseRollbackDto> RollbackReleaseAsync(int releaseId, RollbackReleaseRequest request, CancellationToken ct = default);
    Task<ReleaseDto> PromoteReleaseAsync(int releaseId, CancellationToken ct = default);
    Task<ReleaseDto> CreateReleaseFromPipelineAsync(int projectId, int pipelineRunId, string version, string? changelog,
        string? commitHash = null, string? tagName = null, string? branchName = null,
        int? artifactPipelineRunId = null, bool deployed = false, CancellationToken ct = default);
    Task HandleWebhookAsync(WebhookPayload payload, CancellationToken ct = default);
    bool ValidateWebhookSignature(string? signature, string secret, string rawBody);
    Task NotifyPipelineRunCompletedAsync(int pipelineRunId, PipelineStatus status, CancellationToken ct = default);
    Task NotifyRollbackDeploymentSucceededAsync(int rollbackId, CancellationToken ct = default);
}
