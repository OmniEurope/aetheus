// SPDX-License-Identifier: EUPL-1.2
using System.Text.Json;

namespace Aetheus.Back.Components.Analysis;

internal static class AnalysisJsonValueReader
{
    public static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    public static int? ReadPositiveInt(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.TryGetInt32(out var number) && number > 0
            ? number
            : null;

    public static string? NormalizePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Replace('\\', '/');
        var sourceIndex = normalized.IndexOf("/src/", StringComparison.OrdinalIgnoreCase);
        if (sourceIndex >= 0) normalized = normalized[(sourceIndex + 5)..];
        normalized = normalized.TrimStart('/');
        return normalized.Length <= 1000 ? normalized : normalized[..1000];
    }
}
