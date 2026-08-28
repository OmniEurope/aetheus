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

        var roots = await repository.GetRootRunReferencesAsync(runIds, ct).ConfigureAwait(false);
        if (roots is null || roots.Count == 0)
            return mapped;

        return mapped.Select(release =>
        {
            if (!release.PipelineRunId.HasValue
                || !roots.TryGetValue(release.PipelineRunId.Value, out var root))
                return release;

            return release with
            {
                SourcePipelineId = root.PipelineId,
                SourcePipelineName = root.PipelineName
            };
        }).ToList();
    }
}
