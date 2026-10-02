// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Components.Pipelines;

/// <summary>
/// Read port for pipeline-run ancestry. Keeping it explicit makes the facade composition visible
/// in DI and prevents repositories from silently constructing concrete data collaborators.
/// </summary>
public interface IPipelineRunLineageReader
{
    Task<Dictionary<int, PipelineRunRootReference>> GetRootRunReferencesAsync(
        IReadOnlyCollection<int> runIds,
        CancellationToken ct = default);

    /// <summary>When the run started and, once it has, ended; null for an unknown run.</summary>
    Task<PipelineRunWindow?> GetRunWindowAsync(int runId, CancellationToken ct = default);

    /// <summary>Recette R-498, R2-026: the follow-up runs started by <paramref name="runId"/> (an
    /// <c>on_success</c> chain), among those started between the two instants, oldest first.</summary>
    Task<List<PipelineRunLinkDto>> GetDownstreamRunsAsync(
        int runId, DateTime startedFromUtc, DateTime startedToUtc, CancellationToken ct = default);
}
