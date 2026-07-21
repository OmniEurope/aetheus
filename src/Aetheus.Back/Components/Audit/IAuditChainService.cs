// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.DTOs;

namespace Aetheus.Back.Components.Audit;

public interface IAuditChainService
{
    string ComputeHash(AuditLog entry, string previousHash);
    Task<AuditChainVerificationResult> VerifyChainAsync(CancellationToken ct = default);

    /// <summary>
    /// Verifies the hash-chain from the genesis up to and including the entry with <paramref name="entryId"/>.
    /// Returns null when no entry with that id exists. IsValid=true means every entry up to it is intact.
    /// </summary>
    Task<AuditChainVerificationResult?> VerifyUpToEntryAsync(int entryId, CancellationToken ct = default);
}
