// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.Releases;

public interface IReleaseProvenanceService
{
    /// <summary>Recette R-366/R-367: the release's creator, its later uses and its build inputs.
    /// Null when the release does not exist.</summary>
    Task<ReleaseProvenanceDto?> GetProvenanceAsync(int releaseId, CancellationToken ct = default);
}
