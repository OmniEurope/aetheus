// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Shared.Components.Pipelines;

/// <summary>
/// The shape of a run variable name a typed step publishes or reads (<c>A-Z</c>, <c>0-9</c>,
/// <c>_</c>, not starting with a digit). The control plane checks it when it builds the task and the
/// agent checks it again when it runs it, so both sides must use this one definition.
/// </summary>
public static class PipelineRunVariableName
{
    public static bool IsValid(string value)
    {
        foreach (var character in value)
            if (character is not ((>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_'))
                return false;
        return value.Length > 0 && value[0] is not (>= '0' and <= '9');
    }
}
