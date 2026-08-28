// SPDX-License-Identifier: EUPL-1.2
using System.Text.Json;

namespace Aetheus.Agent.Core.Operations;

internal static class PipelineTargetPatterns
{
    internal static List<string>? ParseOptional(string target)
    {
        if (string.IsNullOrWhiteSpace(target)) return null;
        try { return JsonSerializer.Deserialize<List<string>>(target); }
        catch (JsonException) { return [target]; }
    }
}
