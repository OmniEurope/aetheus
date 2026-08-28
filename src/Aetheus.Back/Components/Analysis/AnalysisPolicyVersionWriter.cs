// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Analysis;

internal static class AnalysisPolicyVersionWriter
{
    public static async Task SaveNewVersionAsync(
        IAnalysisRepository repository,
        AnalysisPolicy policy,
        UpsertAnalysisPolicyRequest request,
        DateTime now,
        CancellationToken ct)
    {
        await repository.SavePolicyRevisionAsync(policy, now, ct).ConfigureAwait(false);
        AnalysisPolicyRequestValidator.Apply(policy, request);
        policy.Version++;
        policy.UpdatedAt = now;
        await repository.SavePolicyAsync(policy, ct).ConfigureAwait(false);
        await repository.SavePolicyRevisionAsync(policy, now, ct).ConfigureAwait(false);
    }
}
