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
}
