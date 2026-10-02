// SPDX-License-Identifier: EUPL-1.2
using System.Text.RegularExpressions;

namespace Aetheus.Front.Components.Pipelines;

internal sealed record PipelineTemplateReference(string Name, int? Version);

internal static partial class PipelineTemplateReferenceHelper
{
    [GeneratedRegex("(?im)^(?<prefix>\\s*extends\\s*:\\s*)(?<quote>['\\\"]?)(?<reference>[^'\\\"#\\r\\n]+)\\k<quote>(?<suffix>\\s*(?:#.*)?)$")]
    private static partial Regex ExtendsLinePattern();

    public static PipelineTemplateReference? Parse(string yaml)
    {
        var match = ExtendsLinePattern().Match(yaml);
        if (!match.Success)
            return null;
        var value = match.Groups["reference"].Value.Trim();
        var separator = value.LastIndexOf('@');
        if (separator > 0 && int.TryParse(value[(separator + 1)..], out var version))
            return new PipelineTemplateReference(value[..separator], version);
        return new PipelineTemplateReference(value, null);
    }

    public static string Pin(string yaml, string name, int version) =>
        ExtendsLinePattern().Replace(yaml, match =>
        {
            var suffix = match.Groups["suffix"].Value;
            if (suffix.StartsWith('#')) suffix = $" {suffix}";
            return $"{match.Groups["prefix"].Value}'{name.Replace("'", "''", StringComparison.Ordinal)}@{version}'{suffix}";
        }, 1);
}
