// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Pipelines;

internal static class PipelineStageTemplateMerger
{
    public static List<PipelineStageDefinition> Append(
        IReadOnlyList<PipelineStageDefinition> existingStages,
        IReadOnlyList<PipelineStageDefinition> templateStages)
    {
        var stages = existingStages.ToList();
        var existingNames = stages.Select(stage => stage.Name).ToHashSet();
        var nameMap = BuildUniqueNameMap(templateStages, existingNames);
        var validNames = new HashSet<string>(existingNames);
        validNames.UnionWith(nameMap.Values);

        foreach (var templateStage in templateStages)
        {
            var dependencies = templateStage.DependsOn
                .Select(dependency => nameMap.GetValueOrDefault(dependency, dependency))
                .Where(validNames.Contains)
                .ToList();
            stages.Add(templateStage with
            {
                Name = nameMap[templateStage.Name],
                DependsOn = dependencies
            });
        }

        return stages;
    }

    private static Dictionary<string, string> BuildUniqueNameMap(
        IReadOnlyList<PipelineStageDefinition> templateStages,
        HashSet<string> existingNames)
    {
        var nameMap = new Dictionary<string, string>();
        foreach (var templateStage in templateStages)
        {
            var baseName = templateStage.Name;
            var uniqueName = baseName;
            var suffix = 2;
            while (existingNames.Contains(uniqueName) || nameMap.ContainsValue(uniqueName))
                uniqueName = $"{baseName}-{suffix++}";
            nameMap[baseName] = uniqueName;
        }

        return nameMap;
    }
}
