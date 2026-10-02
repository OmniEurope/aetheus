// SPDX-License-Identifier: EUPL-1.2
using System.Text.RegularExpressions;

namespace Aetheus.Front.Components.Pipelines;

/// <summary>Splits a pipeline YAML definition into lines, flagging each <c>pipeline: &lt;name&gt;</c>
/// reference so the read-only YAML viewers (run YAML tab + edit-page peek) can render those as clickable
/// links that open the referenced pipeline's definition. Shared so both viewers parse identically.</summary>
internal static partial class PipelineYamlLines
{
    public sealed record Line(string Text, string? PipelineRef);

    [GeneratedRegex(@"^(?<prefix>\s*(?:-\s*)?pipeline:\s*)(?<name>\S+)\s*$")]
    private static partial Regex RefRegex();

    public static List<Line> Parse(string? yaml)
    {
        if (string.IsNullOrEmpty(yaml)) return [];
        var lines = yaml.Replace("\r\n", "\n").Split('\n');
        var result = new List<Line>(lines.Length);
        foreach (var line in lines)
        {
            var m = RefRegex().Match(line);
            result.Add(m.Success
                ? new Line(m.Groups["prefix"].Value, m.Groups["name"].Value.Trim('"', '\''))
                : new Line(line, null));
        }
        return result;
    }
}
