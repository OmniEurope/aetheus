// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.Pipelines;

/// <summary>
/// PLAN-003 lot 30 / D22: creates, in one act, the libraries and vaults the selected templates declare
/// in <c>requires:</c> and the project does not have yet. Only names are copied from a same-named
/// resource elsewhere; values are left empty for the operator.
/// </summary>
public interface IPipelineRequirementsProvisioner
{
    /// <param name="readableLibraryIds">Libraries the caller may read; null means all (admin). The key
    /// names are only ever copied from one of these.</param>
    /// <param name="readableVaultIds">Vaults the caller may read; null means all.</param>
    Task<PipelineRequirementsProvisionResultDto> ProvisionAsync(
        int projectId,
        IReadOnlyList<string> templateNames,
        int? organizationId,
        List<int>? readableLibraryIds,
        List<int>? readableVaultIds,
        CancellationToken ct = default);

    /// <summary>PLAN-003 lot 30: the libraries and vaults the project's own pipelines require and the
    /// project lacks, each with the pipelines waiting for it.</summary>
    Task<List<UnmetRequirementDto>> GetUnmetAsync(int projectId, int? organizationId, CancellationToken ct = default);

    /// <summary>PLAN-003 lot 30: creates what <see cref="GetUnmetAsync"/> reports, keys only, values
    /// empty. The overview's one-click fix for pipelines already created.</summary>
    Task<PipelineRequirementsProvisionResultDto> ProvisionUnmetAsync(
        int projectId,
        int? organizationId,
        List<int>? readableLibraryIds,
        List<int>? readableVaultIds,
        CancellationToken ct = default);
}
