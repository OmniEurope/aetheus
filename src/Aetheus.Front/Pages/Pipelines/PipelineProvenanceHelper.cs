// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.DTOs;

namespace Aetheus.Front.Pages.Pipelines;

internal static class PipelineProvenanceHelper
{
    public static List<PipelineProvenanceItem> Analyze(
        PipelineYamlDefinition definition, PipelineYamlDefinition baseDefinition)
    {
        var result = new List<PipelineProvenanceItem>();
        foreach (var baseStage in baseDefinition.Stages)
        {
            var childStage = Find(definition.Stages, baseStage.Name, stage => stage.Name);
            var stagePath = $"stage:{baseStage.Name}";
            result.Add(new PipelineProvenanceItem(stagePath, childStage is null
                ? PipelineElementProvenance.Inherited
                : PipelineElementProvenance.Overridden));
            AnalyzeSteps(result, stagePath, baseStage.Steps, childStage?.Steps ?? []);
            AnalyzeJobs(result, stagePath, baseStage.Jobs, childStage?.Jobs ?? []);
        }

        foreach (var childStage in definition.Stages.Where(stage =>
                     Find(baseDefinition.Stages, stage.Name, item => item.Name) is null))
        {
            var stagePath = $"stage:{childStage.Name}";
            result.Add(new PipelineProvenanceItem(stagePath, PipelineElementProvenance.Local));
            AddLocalSteps(result, stagePath, childStage.Steps);
            foreach (var job in childStage.Jobs)
            {
                var jobPath = $"{stagePath}/job:{job.Name}";
                result.Add(new PipelineProvenanceItem(jobPath, PipelineElementProvenance.Local));
                AddLocalSteps(result, jobPath, job.Steps);
            }
        }
        return result;
    }

    private static void AnalyzeJobs(
        List<PipelineProvenanceItem> result,
        string stagePath,
        IReadOnlyList<PipelineJobDefinition> baseJobs,
        IReadOnlyList<PipelineJobDefinition> childJobs)
    {
        foreach (var baseJob in baseJobs)
        {
            var childJob = Find(childJobs, baseJob.Name, job => job.Name);
            var jobPath = $"{stagePath}/job:{baseJob.Name}";
            result.Add(new PipelineProvenanceItem(jobPath, childJob is null
                ? PipelineElementProvenance.Inherited
                : PipelineElementProvenance.Overridden));
            AnalyzeSteps(result, jobPath, baseJob.Steps, childJob?.Steps ?? []);
        }
        foreach (var childJob in childJobs.Where(job => Find(baseJobs, job.Name, item => item.Name) is null))
        {
            var jobPath = $"{stagePath}/job:{childJob.Name}";
            result.Add(new PipelineProvenanceItem(jobPath, PipelineElementProvenance.Local));
            AddLocalSteps(result, jobPath, childJob.Steps);
        }
    }

    private static void AnalyzeSteps(
        List<PipelineProvenanceItem> result,
        string parentPath,
        IReadOnlyList<PipelineStepDefinition> baseSteps,
        IReadOnlyList<PipelineStepDefinition> childSteps)
    {
        foreach (var baseStep in baseSteps)
        {
            var childStep = Find(childSteps, baseStep.Name, step => step.Name);
            result.Add(new PipelineProvenanceItem($"{parentPath}/step:{baseStep.Name}", childStep is null
                ? PipelineElementProvenance.Inherited
                : PipelineElementProvenance.Overridden));
        }
        AddLocalSteps(result, parentPath, childSteps.Where(step =>
            Find(baseSteps, step.Name, item => item.Name) is null));
    }

    private static void AddLocalSteps(
        List<PipelineProvenanceItem> result,
        string parentPath,
        IEnumerable<PipelineStepDefinition> steps)
    {
        result.AddRange(steps.Select(step =>
            new PipelineProvenanceItem($"{parentPath}/step:{step.Name}", PipelineElementProvenance.Local)));
    }

    private static T? Find<T>(IReadOnlyList<T> items, string name, Func<T, string> selector) where T : class =>
        items.FirstOrDefault(item => string.Equals(selector(item), name, StringComparison.OrdinalIgnoreCase));
}
