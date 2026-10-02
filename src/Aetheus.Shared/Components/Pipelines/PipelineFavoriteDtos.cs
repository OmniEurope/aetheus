// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Shared.Components.Pipelines;

public sealed record PipelineFavoritesDto
{
    public List<int> PipelineIds { get; init; } = [];
}

public sealed record PipelineFavoriteDto
{
    public int PipelineId { get; init; }
    public bool IsFavorite { get; init; }
}

public sealed record SetPipelineFavoriteRequest
{
    public bool IsFavorite { get; init; }
}
