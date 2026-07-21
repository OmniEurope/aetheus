// SPDX-License-Identifier: EUPL-1.2
using System.Text.Json;

namespace Aetheus.Back.Components.Shared;

public static class TagsHelper
{
    public static List<string> DeserializeTags(string tags)
    {
        if (string.IsNullOrWhiteSpace(tags)) return [];
        try { return JsonSerializer.Deserialize<List<string>>(tags) ?? []; }
        // Legacy/corrupt tag rows return empty; the calling controller already validates new writes via SerializeTags.
        catch (JsonException) { return []; }
    }

    public static string SerializeTags(IEnumerable<string>? tags)
    {
        var list = tags?.Where(t => !string.IsNullOrWhiteSpace(t)).ToList() ?? [];
        return list.Count == 0 ? string.Empty : JsonSerializer.Serialize(list);
    }
}
