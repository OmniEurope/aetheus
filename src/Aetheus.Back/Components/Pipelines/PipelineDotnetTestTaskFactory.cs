// SPDX-License-Identifier: EUPL-1.2
using System.Globalization;
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Pipelines;

/// <summary>
/// Turns a <c>type: dotnet-test</c> step into the agent task that runs it (PLAN-006 lot 11.3).
///
/// The step's whole reason to exist is the classification the agent applies to the result, so the
/// only thing decided here is what the agent is told: which project, where the evidence goes, and
/// which run variable the classified status is published under.
/// </summary>
public interface IPipelineDotnetTestTaskFactory
{
    /// <summary><c>null</c> when the task was created; otherwise the validation error the caller must
    /// fail the run with. Finalizing a run stays with the engine, so this stays a factory.</summary>
    string? CreateDotnetTestTask(
        int runId, Server legServer, PipelineStepRun stepRun, PipelineStepDefinition stepDef,
        Dictionary<string, string> legVars, PipelineStageDefinition stageDef);
}

/// <inheritdoc cref="IPipelineDotnetTestTaskFactory"/>
public sealed class PipelineDotnetTestTaskFactory(IPipelineStepTaskBuilder taskBuilder)
    : IPipelineDotnetTestTaskFactory
{
    public string? CreateDotnetTestTask(
        int runId, Server legServer, PipelineStepRun stepRun, PipelineStepDefinition stepDef,
        Dictionary<string, string> legVars, PipelineStageDefinition stageDef)
    {
        ArgumentNullException.ThrowIfNull(stepDef);
        ArgumentNullException.ThrowIfNull(legVars);
        ArgumentNullException.ThrowIfNull(stageDef);

        if (string.IsNullOrWhiteSpace(stepDef.TestProject))
            return $"Step '{stepDef.Name}': a dotnet-test step needs a 'test_project'.";
        if (string.IsNullOrWhiteSpace(stepDef.ResultsDirectory))
            return $"Step '{stepDef.Name}': a dotnet-test step needs a 'results_directory'.";
        if (string.IsNullOrWhiteSpace(stepDef.StatusVariable))
            return $"Step '{stepDef.Name}': a dotnet-test step needs a 'status_variable', which is "
                + "the run variable its gate reads.";

        var variables = new Dictionary<string, string>(legVars, StringComparer.OrdinalIgnoreCase)
        {
            ["AETHEUS_RUN_ID"] = runId.ToString(CultureInfo.InvariantCulture),
            ["AETHEUS_STAGE_NAME"] = stageDef.Name,
            [PipelineDotnetTestVariables.ResultsDirectory] = stepDef.ResultsDirectory,
            [PipelineDotnetTestVariables.StatusVariable] = stepDef.StatusVariable,
            [PipelineDotnetTestVariables.CollectCoverage] =
                stepDef.CollectCoverage ? "true" : "false"
        };
        if (!string.IsNullOrWhiteSpace(stepDef.TrxName))
            variables[PipelineDotnetTestVariables.TrxName] = stepDef.TrxName;
        if (!string.IsNullOrWhiteSpace(stepDef.RunSettings))
            variables[PipelineDotnetTestVariables.RunSettings] = stepDef.RunSettings;
        if (!string.IsNullOrWhiteSpace(stepDef.Configuration))
            variables[PipelineDotnetTestVariables.Configuration] = stepDef.Configuration;

        // The project path travels as the task target rather than in the environment, so it goes
        // through the same target validation as every other typed step.
        taskBuilder.TrackTypedTask(
            runId, legServer, stepRun, stepDef, stepDef.TestProject, variables,
            OperationKind.PipelineDotnetTest);
        return null;
    }
}
