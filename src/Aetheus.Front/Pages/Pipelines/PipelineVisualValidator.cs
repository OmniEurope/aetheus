// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Pipelines;

internal sealed class PipelineVisualValidator(IStringLocalizer<AppStrings> localizer)
{
    public List<string> GetStageWarnings(
        PipelineStageDefinition stage, PipelineYamlDefinition definition)
    {
        var warnings = new List<string>();
        if (string.IsNullOrWhiteSpace(stage.Agent))
            warnings.Add(localizer["ValidationMissingAgent"]);
        if (stage.Steps.Count == 0)
            warnings.Add(localizer["ValidationNoSteps"]);
        foreach (var dependency in stage.DependsOn)
        {
            if (definition.Stages.All(candidate => candidate.Name != dependency))
                warnings.Add(string.Format(localizer["ValidationUnknownDependency"], dependency));
        }
        if (HasCircularDependency(stage.Name, definition))
            warnings.Add(localizer["ValidationCircularDependency"]);
        return warnings;
    }

    internal static bool HasCircularDependency(string stageName, PipelineYamlDefinition definition)
    {
        var stages = definition.Stages.ToDictionary(stage => stage.Name, StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);

        bool Visit(string current)
        {
            if (visiting.Contains(current)) return true;
            if (!visited.Add(current) || !stages.TryGetValue(current, out var stage)) return false;
            visiting.Add(current);
            foreach (var dependency in stage.DependsOn)
            {
                if (Visit(dependency)) return true;
            }
            visiting.Remove(current);
            return false;
        }

        return Visit(stageName);
    }
}
