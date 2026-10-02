// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Shared.Components.Pipelines;

/// <summary>
/// The environment-variable names a <c>type: gate-status</c> step travels under, shared between the
/// control plane that writes them and the agent that reads them, for the same reason as
/// <see cref="PipelineDotnetTestVariables"/>: a name spelled differently on one side would not fail
/// to compile, it would produce a gate that quietly ignores its own configuration.
/// </summary>
public static class PipelineGateStatusVariables
{
    /// <summary>Comma-separated names of the run variables to aggregate.</summary>
    public const string StatusVariables = "AETHEUS_GATE_STATUS_VARIABLES";

    /// <summary>The run variable the aggregated verdict is published under. Optional.</summary>
    public const string PublishAs = "AETHEUS_GATE_PUBLISH_AS";

    /// <summary><c>true</c> when a non-zero verdict must fail the step.</summary>
    public const string Blocking = "AETHEUS_GATE_BLOCKING";

    /// <summary>What this gate is about, for the step's log line.</summary>
    public const string Label = "AETHEUS_GATE_LABEL";
}
