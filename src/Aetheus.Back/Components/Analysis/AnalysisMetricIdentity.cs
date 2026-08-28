// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Components.Analysis;

internal static class AnalysisMetricIdentity
{
    public static string Build(string key, string? scope, string? language, string? filePath, string? symbol) =>
        string.Join('|', Normalize(key), Normalize(scope), Normalize(language), NormalizePath(filePath), Normalize(symbol));

    private static string Normalize(string? value) => value?.Trim().ToLowerInvariant() ?? string.Empty;
    private static string NormalizePath(string? value) => Normalize(value).Replace('\\', '/');
}
