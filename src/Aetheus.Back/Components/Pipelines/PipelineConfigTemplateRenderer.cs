// SPDX-License-Identifier: EUPL-1.2
using System.Text.RegularExpressions;

namespace Aetheus.Back.Components.Pipelines;

internal static class PipelineConfigTemplateRenderer
{
    private const int MaxRenderedLength = 256 * 1024;
    private static readonly Regex TokenRegex = new(
        @"#\{([A-Za-z_][A-Za-z0-9_.-]*)\}#",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    public static bool IsSafeConfigPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var normalized = path.Replace('\\', '/').Trim();
        return normalized.StartsWith(".pipeline/configs/", StringComparison.Ordinal)
            && normalized.Length <= 240
            && !normalized.Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Any(segment => segment is "." or "..");
    }

    public static string RenderStrict(
        string template,
        IReadOnlyDictionary<string, string> variables)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(variables);

        var rendered = TokenRegex.Replace(template, match =>
        {
            var name = match.Groups[1].Value;
            if (!variables.TryGetValue(name, out var value))
                throw new BadRequestException(
                    $"Configuration template variable '{name}' is not defined.");
            if (value.IndexOfAny(['\r', '\n', '\0']) >= 0)
                throw new BadRequestException(
                    $"Configuration template variable '{name}' contains a forbidden line break or NUL byte.");
            return value;
        });

        if (rendered.Contains("#{", StringComparison.Ordinal))
            throw new BadRequestException("Configuration template contains an invalid or unresolved token.");
        if (rendered.Length > MaxRenderedLength)
            throw new BadRequestException(
                $"Rendered configuration exceeds the {MaxRenderedLength} character safety limit.");
        return rendered;
    }
}
