// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.Pipelines;

internal static class PipelinePathValidation
{
    internal static bool IsSafeRelativeDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return true;
        if (Path.IsPathRooted(path)) return false;
        return !path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment is "." or "..");
    }
}
