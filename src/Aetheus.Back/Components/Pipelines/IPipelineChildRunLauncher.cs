// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.DTOs;

namespace Aetheus.Back.Components.Pipelines;

/// <summary>
/// Starts a child run. This is the one edge that closes the engine's real recursion - a `type: trigger`
/// step starts a run, whose own trigger steps start further runs - so it is supplied as a PARAMETER,
/// never injected: the coordinator would otherwise depend on the launcher that depends on the
/// dispatcher that depends on the coordinator.
/// </summary>
public interface IPipelineChildRunLauncher
{
    /// <inheritdoc cref="IPipelineRunService.TriggerPreparedRunAsync"/>
    Task<PipelineRunDto?> TriggerPreparedRunAsync(
        PipelineRunPreparation preparation,
        Dictionary<string, string>? additionalVariables = null,
        Dictionary<string, string>? parameters = null,
        CancellationToken ct = default,
        string? idempotencyKey = null);
}
