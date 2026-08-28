// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.Pipelines;

/// <summary>
/// Validates the AI-specific contract of a typed pipeline step.
/// </summary>
internal static class PipelineAiStepValidator
{
    public static void Validate(
        List<string> errors,
        PipelineStepDefinition step,
        string context)
    {
        if (!string.Equals(step.Type, "ai", StringComparison.OrdinalIgnoreCase)) return;

        if (string.IsNullOrWhiteSpace(step.Profile))
            errors.Add($"AI step '{step.Name}' in {context} must select a profile.");
        if (string.IsNullOrWhiteSpace(step.Prompt) == string.IsNullOrWhiteSpace(step.PromptFile))
            errors.Add($"AI step '{step.Name}' in {context} must set exactly one of prompt or prompt_file.");
        if (step.PromptFile is not null && !IsSafeWorkspaceRelativePath(step.PromptFile))
            errors.Add($"AI step '{step.Name}' in {context} has an unsafe prompt_file.");
        if (step.ContextPaths.Count > 32
            || step.ContextPaths.Any(path => !IsSafeWorkspaceRelativePath(path)))
            errors.Add($"AI step '{step.Name}' in {context} has invalid context_paths.");
    }

    private static bool IsSafeWorkspaceRelativePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value) || value.Length > 260)
            return false;
        return !value.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment is "." or "..");
    }
}
