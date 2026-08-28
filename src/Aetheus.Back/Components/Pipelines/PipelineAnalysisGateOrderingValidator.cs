// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.Analysis;

namespace Aetheus.Back.Components.Pipelines;

internal static class PipelineAnalysisGateOrderingValidator
{
    private static readonly HashSet<string> QualityStepTypes =
        new(["coverage", "complexity", "lint"], StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> ProtectedStepTypes =
        new(["artifacts", "release", "deploy"], StringComparer.OrdinalIgnoreCase);

    public static List<string> Validate(IReadOnlyList<PipelineStageDefinition> stages)
    {
        var errors = new List<string>();
        var stageMap = stages
            .Where(stage => !string.IsNullOrWhiteSpace(stage.Name))
            .GroupBy(stage => stage.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var gates = FindGates(stages);
        foreach (var duplicateScope in gates
                     .GroupBy(gate => gate.Scope, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
            errors.Add($"A pipeline can declare only one {duplicateScope.Key} analysis gate.");

        foreach (var gate in gates)
        {
            foreach (var producer in FindProducers(stages, gate.Scope))
            {
                if (RunsBefore(producer, gate, stageMap)) continue;
                errors.Add(
                    $"Analysis gate '{gate.Step.Name}' ({gate.Scope}) must run after producer " +
                    $"'{producer.Step.Name}' in stage '{producer.Stage.Name}'.");
            }
        }

        foreach (var protectedStage in stages.Where(IsProtectedStage))
        {
            foreach (var gate in gates)
            {
                if (IsAncestor(gate.Stage.Name, protectedStage.Name, stageMap)) continue;
                errors.Add(
                    $"Protected stage '{protectedStage.Name}' must depend on analysis gate " +
                    $"'{gate.Step.Name}' ({gate.Scope}) before packaging, release or deployment.");
            }
        }

        return errors;
    }

    private static List<AnalysisStep> FindGates(IEnumerable<PipelineStageDefinition> stages) =>
        stages.SelectMany(stage => stage.Steps.Select((step, index) => new AnalysisStep(stage, step, index)))
            .Where(candidate => string.Equals(
                candidate.Step.Type, "analysis-gate", StringComparison.OrdinalIgnoreCase))
            .Select(candidate => candidate with
            {
                Scope = candidate.Step.AnalysisScope?.ToLowerInvariant() ?? string.Empty
            })
            .Where(candidate => candidate.Scope is "quality" or "security")
            .ToList();

    private static IEnumerable<AnalysisStep> FindProducers(
        IEnumerable<PipelineStageDefinition> stages,
        string scope) =>
        stages.SelectMany(stage => stage.Steps.Select((step, index) => new AnalysisStep(stage, step, index)))
            .Where(candidate => string.Equals(GetProducerScope(candidate.Step), scope, StringComparison.Ordinal));

    private static string? GetProducerScope(PipelineStepDefinition step)
    {
        if (step.Type is not null && QualityStepTypes.Contains(step.Type))
            return "quality";
        if (!string.Equals(step.Type, "scanner", StringComparison.OrdinalIgnoreCase))
            return null;

        var category = string.IsNullOrWhiteSpace(step.Scanner)
            ? null
            : ScannerManifestCatalog.Find(step.Scanner)?.Category;
        return AnalysisGateScopes.ForScannerCategory(category);
    }

    private static bool IsProtectedStage(PipelineStageDefinition stage) =>
        stage.Artifacts.Count > 0
        || stage.Steps.Any(step => step.Type is not null && ProtectedStepTypes.Contains(step.Type));

    private static bool RunsBefore(
        AnalysisStep producer,
        AnalysisStep gate,
        IReadOnlyDictionary<string, PipelineStageDefinition> stageMap)
    {
        if (string.Equals(producer.Stage.Name, gate.Stage.Name, StringComparison.OrdinalIgnoreCase))
            return producer.StepIndex < gate.StepIndex;
        return IsAncestor(producer.Stage.Name, gate.Stage.Name, stageMap);
    }

    private static bool IsAncestor(
        string ancestorName,
        string descendantName,
        IReadOnlyDictionary<string, PipelineStageDefinition> stageMap)
    {
        if (!stageMap.TryGetValue(descendantName, out var descendant)) return false;
        var pending = new Stack<string>(descendant.DependsOn);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (pending.TryPop(out var dependency))
        {
            if (!visited.Add(dependency)) continue;
            if (string.Equals(dependency, ancestorName, StringComparison.OrdinalIgnoreCase))
                return true;
            if (stageMap.TryGetValue(dependency, out var stage))
            {
                foreach (var transitiveDependency in stage.DependsOn)
                    pending.Push(transitiveDependency);
            }
        }

        return false;
    }

    private sealed record AnalysisStep(
        PipelineStageDefinition Stage,
        PipelineStepDefinition Step,
        int StepIndex,
        string Scope = "");
}
