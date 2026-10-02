// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.Pipelines;

internal static class ReleaseSourcePipelineEnricher
{
    public static async Task<List<ReleaseDto>> EnrichAsync(
        IReadOnlyCollection<ReleaseDto> releases,
        IPipelineRepository? repository,
        CancellationToken ct)
    {
        var mapped = releases.ToList();
        if (repository is null || mapped.Count == 0)
            return mapped;

        var runIds = mapped
            .Where(release => release.PipelineRunId.HasValue)
            .Select(release => release.PipelineRunId!.Value)
            .Distinct()
            .ToList();
        if (runIds.Count == 0)
            return mapped;

        var roots = await repository.GetRootRunReferencesAsync(runIds, ct).ConfigureAwait(false) ?? [];
        var statuses = await repository.GetRunStatusesByIdsAsync(runIds, ct).ConfigureAwait(false) ?? [];

        return mapped.Select(release =>
        {
            if (!release.PipelineRunId.HasValue) return release;
            var runId = release.PipelineRunId.Value;
            if (statuses.TryGetValue(runId, out var status))
                release = release with { PipelineRunStatus = status };
            return roots.TryGetValue(runId, out var root)
                ? release with { SourcePipelineId = root.PipelineId, SourcePipelineName = root.PipelineName }
                : release;
        }).ToList();
    }
}
