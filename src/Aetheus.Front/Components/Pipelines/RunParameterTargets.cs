// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Pipelines;

/// <summary>
/// PLAN-005 lot 5 / D37: which declared parameter a picked release fills. One definition, read by the
/// launch dialogs (to know whether releases are worth loading at all) and by the parameter fields (to
/// know which field a click fills): a required string parameter with no default, and only when there
/// is exactly one, since a click must never guess between two.
/// </summary>
internal static class RunParameterTargets
{
    internal static string? ReleaseFillTarget(IEnumerable<PipelineRunParameterDto> parameters) =>
        parameters
            .Where(p => p.Required
                && string.IsNullOrWhiteSpace(p.Default)
                && (string.IsNullOrEmpty(p.Type) || p.Type.Equals("string", StringComparison.OrdinalIgnoreCase)))
            .Select(p => p.Name)
            .ToList() is [var single] ? single : null;
}
