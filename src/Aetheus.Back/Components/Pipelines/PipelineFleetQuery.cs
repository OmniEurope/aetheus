// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Shared;

namespace Aetheus.Back.Components.Pipelines;

/// <summary>
/// Recette R-224: the column header filters of the pipeline fleet, which replace the template and
/// status dropdowns that sat above the grid. The fleet row is a projection (owner and freshness are
/// computed), so the map reads the projected row; each key is the grid column's key.
/// </summary>
internal static class PipelineFleetQuery
{
    internal static readonly GridQueryMap<PipelineFleetItemDto> Columns = new GridQueryMap<PipelineFleetItemDto>()
        .Text("pipelineName", item => item.PipelineName)
        .Text("ownerName", item => item.OwnerName)
        .Text("templateName", item => item.TemplateName)
        .Number("pinnedVersion", item => item.PinnedVersion)
        .Number("latestVersion", item => item.LatestVersion)
        .Enum("freshness", item => item.Freshness);
}
