// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.Pipelines;

internal static class PipelineBranchValidator
{
    public static bool IsValid(string branch) => !string.IsNullOrWhiteSpace(branch)
        && branch.Length <= 255
        && branch[0] != '-'
        && branch[0] != '/'
        && branch[^1] != '/'
        && !branch.Contains("..", StringComparison.Ordinal)
        && !branch.Contains("@{", StringComparison.Ordinal)
        && !branch.Contains('\\')
        && branch.All(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-' or '/');
}
