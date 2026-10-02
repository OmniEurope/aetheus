// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Pipelines;

/// <summary>
/// Reads the definition a run must execute. Extracted from <see cref="PipelineRunService"/> because the
/// engine, the control service and later the scheduler all need it, and
/// <see cref="PipelineRunHelpers"/> is within ten lines of its 600-line budget.
/// </summary>
public interface IPipelineRunDefinitionParser
{
    /// <summary>The run's captured definition, or <c>null</c> when it cannot be parsed.</summary>
    PipelineYamlDefinition? Parse(PipelineRun run);
}

/// <inheritdoc cref="IPipelineRunDefinitionParser"/>
public sealed class PipelineRunDefinitionParser(
    ILogger<PipelineRunDefinitionParser> logger) : IPipelineRunDefinitionParser
{
    /// <summary>
    /// F-012 (ADR-015): a run executes the YAML captured at trigger time - never the live
    /// definition, which may be edited mid-run. Legacy runs without a snapshot fall back to the
    /// pipeline's current definition.
    /// </summary>
    public PipelineYamlDefinition? Parse(PipelineRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(run.Pipeline, nameof(run.Pipeline));
        var yaml = string.IsNullOrWhiteSpace(run.YamlSnapshot) ? run.Pipeline.YamlDefinition : run.YamlSnapshot;
        return YamlParsingHelper.ParseAndValidate(yaml, logger);
    }
}
