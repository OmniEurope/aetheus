// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.DTOs;

namespace Aetheus.Front.Pages.Pipelines;

internal static class PipelineVisualInheritanceHelper
{
    public static List<PipelineStageDefinition> MergeStages(
        IReadOnlyList<PipelineStageDefinition> inheritedStages,
        IReadOnlyList<PipelineStageDefinition> localStages)
    {
        var result = inheritedStages.ToList();
        foreach (var localStage in localStages)
        {
            var index = FindByName(result, localStage.Name, stage => stage.Name);
            if (localStage.Remove)
            {
                if (index >= 0) result.RemoveAt(index);
                continue;
            }

            var merged = index < 0 ? localStage : localStage with
            {
                Jobs = MergeJobs(result[index].Jobs, localStage.Jobs),
                Steps = MergeSteps(result[index].Steps, localStage.Steps)
            };
            if (index >= 0) result[index] = merged; else result.Add(merged);
        }
        return result;
    }

    private static List<PipelineJobDefinition> MergeJobs(
        IReadOnlyList<PipelineJobDefinition> inheritedJobs,
        IReadOnlyList<PipelineJobDefinition> localJobs)
    {
        var result = inheritedJobs.ToList();
        foreach (var localJob in localJobs)
        {
            var index = FindByName(result, localJob.Name, job => job.Name);
            if (localJob.Remove)
            {
                if (index >= 0) result.RemoveAt(index);
                continue;
            }
            var merged = index < 0
                ? localJob
                : localJob with { Steps = MergeSteps(result[index].Steps, localJob.Steps) };
            if (index >= 0) result[index] = merged; else result.Add(merged);
        }
        return result;
    }

    private static List<PipelineStepDefinition> MergeSteps(
        IReadOnlyList<PipelineStepDefinition> inheritedSteps,
        IReadOnlyList<PipelineStepDefinition> localSteps)
    {
        var result = inheritedSteps.ToList();
        foreach (var localStep in localSteps)
        {
            var index = FindByName(result, localStep.Name, step => step.Name);
            if (localStep.Remove)
            {
                if (index >= 0) result.RemoveAt(index);
            }
            else if (index >= 0)
            {
                result[index] = localStep;
            }
            else
            {
                result.Add(localStep);
            }
        }
        return result;
    }

    private static int FindByName<T>(IReadOnlyList<T> items, string name, Func<T, string> selector)
    {
        for (var index = 0; index < items.Count; index++)
            if (string.Equals(selector(items[index]), name, StringComparison.OrdinalIgnoreCase)) return index;
        return -1;
    }
}
