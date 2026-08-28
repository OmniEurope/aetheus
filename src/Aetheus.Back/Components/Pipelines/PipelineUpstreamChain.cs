// SPDX-License-Identifier: EUPL-1.2
using System.Text.Json;

namespace Aetheus.Back.Components.Pipelines;

internal static class PipelineUpstreamChain
{
    internal static IReadOnlyList<int> Read(string? additionalVariablesJson)
    {
        if (string.IsNullOrWhiteSpace(additionalVariablesJson)) return [];
        try
        {
            var variables = JsonSerializer.Deserialize<Dictionary<string, string>>(additionalVariablesJson);
            if (variables is null
                || !variables.TryGetValue("UPSTREAM_CHAIN", out var chain)
                || string.IsNullOrWhiteSpace(chain))
                return [];
            return chain.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(value => int.TryParse(value, out var id) ? id : (int?)null)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
