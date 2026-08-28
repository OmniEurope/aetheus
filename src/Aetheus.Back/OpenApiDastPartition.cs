// SPDX-License-Identifier: EUPL-1.2
using Microsoft.AspNetCore.Mvc.ApiExplorer;

namespace Aetheus.Back;

internal static class OpenApiDastPartition
{
    internal const int Count = 8;

    internal static string DocumentName(int partition) => $"dast-{partition}";

    internal static bool Includes(ApiDescription description, int partition)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(partition);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(partition, Count);

        const uint offsetBasis = 2166136261;
        const uint prime = 16777619;
        var identity = $"{description.HttpMethod}:{description.RelativePath}";
        var hash = offsetBasis;
        foreach (var character in identity)
        {
            hash ^= character;
            hash *= prime;
        }

        return hash % Count == partition;
    }
}
