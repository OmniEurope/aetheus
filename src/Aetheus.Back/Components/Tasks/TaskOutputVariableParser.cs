// SPDX-License-Identifier: EUPL-1.2
using System.Text.RegularExpressions;

namespace Aetheus.Back.Components.Tasks;

internal static partial class TaskOutputVariableParser
{
    [GeneratedRegex(@"##aetheus\[setvariable name=(\w+)\](.+)$", RegexOptions.Multiline)]
    private static partial Regex OutputVariablePattern();

    internal static Dictionary<string, string> Parse(string? output)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(output)) return result;

        foreach (Match match in OutputVariablePattern().Matches(output))
            result[match.Groups[1].Value] = match.Groups[2].Value.Trim();

        return result;
    }
}
