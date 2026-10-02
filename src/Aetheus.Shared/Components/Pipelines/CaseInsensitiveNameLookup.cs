// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Shared.Components.Pipelines;

public static class CaseInsensitiveNameLookup
{
    public static int FindIndex<T>(IReadOnlyList<T> items, string name, Func<T, string> selector)
    {
        for (var index = 0; index < items.Count; index++)
            if (string.Equals(selector(items[index]), name, StringComparison.OrdinalIgnoreCase)) return index;
        return -1;
    }
}
