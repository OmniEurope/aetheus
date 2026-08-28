// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Analysis;

internal sealed class AnalysisPolicyRevisionManager(
    IAnalysisRepository repository,
    TimeProvider timeProvider,
    IAuditService audit)
{
    public async Task<List<AnalysisPolicyRevisionDto>> GetAsync(
        int? organizationId,
        int? projectId,
        int policyId,
        CancellationToken ct)
    {
        var policy = await GetPolicyInScopeAsync(organizationId, projectId, policyId, ct)
            .ConfigureAwait(false);
        return (await repository.GetPolicyRevisionsAsync(policy.Id, ct).ConfigureAwait(false))
            .Select(ToDto)
            .ToList();
    }

    public async Task<AnalysisPolicyDto> RollbackAsync(
        int? organizationId,
        int? projectId,
        int policyId,
        int version,
        CancellationToken ct)
    {
        var policy = await GetPolicyInScopeAsync(organizationId, projectId, policyId, ct)
            .ConfigureAwait(false);
        var revision = await repository.GetPolicyRevisionAsync(policy.Id, version, ct).ConfigureAwait(false)
            ?? throw new NotFoundException("Analysis policy revision not found.");
        if (!string.Equals(
            revision.SnapshotHash,
            AnalysisPolicySnapshot.Hash(revision.SnapshotJson),
            StringComparison.Ordinal))
            throw new InvalidOperationException("The analysis policy revision snapshot is corrupted.");

        var request = AnalysisPolicySnapshot.DeserializeRequest(revision.SnapshotJson);
        AnalysisPolicyRequestValidator.Validate(request);
        var siblings = await repository.GetPoliciesForScopeAsync(policy.OrganizationId, projectId, ct)
            .ConfigureAwait(false);
        if (siblings.Any(item => item.Id != policy.Id
            && string.Equals(item.Name, request.Name, StringComparison.OrdinalIgnoreCase)))
            throw new ConflictException("The restored policy name already exists in the selected scope.");

        var now = timeProvider.GetUtcNow().UtcDateTime;
        await AnalysisPolicyVersionWriter.SaveNewVersionAsync(repository, policy, request, now, ct)
            .ConfigureAwait(false);
        await audit.LogAsync(
            "AnalysisPolicyRolledBack",
            nameof(AnalysisPolicy),
            policy.Id,
            $"restoredVersion={version};newVersion={policy.Version}",
            ct).ConfigureAwait(false);
        return AnalysisMapper.ToDto(policy);
    }

    private async Task<AnalysisPolicy> GetPolicyInScopeAsync(
        int? organizationId,
        int? projectId,
        int policyId,
        CancellationToken ct)
    {
        var policy = await repository.GetPolicyAsync(policyId, ct).ConfigureAwait(false)
            ?? throw new NotFoundException("Analysis policy not found.");
        if (policy.ProjectId != projectId
            || (!projectId.HasValue && policy.OrganizationId != organizationId))
            throw new NotFoundException("Analysis policy not found in the selected scope.");
        return policy;
    }

    private static AnalysisPolicyRevisionDto ToDto(AnalysisPolicyRevision revision) => new()
    {
        PolicyId = revision.AnalysisPolicyId,
        Version = revision.Version,
        SnapshotHash = revision.SnapshotHash,
        CreatedAt = revision.CreatedAt
    };
}
