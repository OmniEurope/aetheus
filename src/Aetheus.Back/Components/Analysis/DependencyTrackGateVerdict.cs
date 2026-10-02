// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Analysis;

/// <summary>
/// When Dependency-Track is required, a run that produced an SBOM is not passed until its upload has
/// succeeded: the gate turns to Error with the outbox state among its missing producers. Read live on
/// every request, never cached: the outbox moves after the run has finished. Extracted from
/// <see cref="AnalysisService"/> so the run result reader applies the same rule (recette R-485).
/// </summary>
internal static class DependencyTrackGateVerdict
{
    public static async Task<AnalysisRunGateDto> ApplyAsync(
        AnalysisRunGateDto gate,
        int runId,
        string? scope,
        DependencyTrackOptions options,
        IAnalysisRepository repository,
        CancellationToken ct)
    {
        if (!options.Enabled || !options.Required
            || AnalysisGateScopes.Normalize(scope) == AnalysisGateScopes.Quality)
            return gate;

        var tracking = await repository.GetDependencyTrackGateStateAsync(runId, ct).ConfigureAwait(false);
        if (!tracking.HasSbom)
            return gate;
        var dependencyTrackState = tracking.Statuses.Count == 0
            ? "dependency-track:missing"
            : tracking.Statuses.Any(status =>
                string.Equals(status, DependencyTrackOutboxStatuses.Failed, StringComparison.Ordinal))
                ? "dependency-track:failed"
                : tracking.Statuses.All(status =>
                    string.Equals(status, DependencyTrackOutboxStatuses.Succeeded, StringComparison.Ordinal))
                    ? null
                    : "dependency-track:pending";
        if (dependencyTrackState is null)
            return gate;

        return gate with
        {
            Status = AnalysisGateStatus.Error,
            MissingProducers = [.. gate.MissingProducers, dependencyTrackState]
        };
    }
}
