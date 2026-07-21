// SPDX-License-Identifier: EUPL-1.2
using System.Security.Cryptography;
using System.Text;
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.DTOs;

namespace Aetheus.Back.Components.Audit;

public class AuditChainService(IAuditRepository repo) : IAuditChainService
{
    /// <summary>
    /// v2 payload (F-003): excludes <see cref="AuditLog.Id"/> so the hash can be computed before
    /// the insert (the row is never UPDATEd - append-only at DB level), and adds EntityType.
    /// Rows written before the v2 cutover are accepted via <see cref="ComputeLegacyHash"/>.
    /// </summary>
    public string ComputeHash(AuditLog entry, string previousHash)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var payload = string.Join('|',
            "v2",
            entry.Timestamp.ToString("O"),
            entry.Username,
            entry.Action,
            entry.EntityType,
            entry.Details ?? string.Empty,
            previousHash);

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexStringLower(hashBytes);
    }

    /// <summary>Pre-v2 formula (included Id, no EntityType) - verification-only.</summary>
    private static string ComputeLegacyHash(AuditLog entry, string previousHash)
    {
        var payload = string.Join('|',
            entry.Id,
            entry.Timestamp.ToString("O"),
            entry.Username,
            entry.Action,
            entry.Details ?? string.Empty,
            previousHash);

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexStringLower(hashBytes);
    }

    public async Task<AuditChainVerificationResult> VerifyChainAsync(CancellationToken ct = default)
    {
        // F-006: streamed verification - bounded memory regardless of table size.
        var previousHash = string.Empty;
        var count = 0;

        await foreach (var entry in repo.StreamOrderedAsync().WithCancellation(ct).ConfigureAwait(false))
        {
            count++;

            if (entry.PreviousHash != previousHash)
            {
                return new AuditChainVerificationResult
                {
                    IsValid = false,
                    TotalEntries = count,
                    FirstInvalidId = entry.Id,
                    ErrorMessage = $"PreviousHash mismatch at entry {entry.Id}"
                };
            }

            if (entry.Hash != ComputeHash(entry, previousHash) &&
                entry.Hash != ComputeLegacyHash(entry, previousHash))
            {
                return new AuditChainVerificationResult
                {
                    IsValid = false,
                    TotalEntries = count,
                    FirstInvalidId = entry.Id,
                    ErrorMessage = $"Hash mismatch at entry {entry.Id}"
                };
            }

            previousHash = entry.Hash;
        }

        return new AuditChainVerificationResult
        {
            IsValid = true,
            TotalEntries = count
        };
    }

    public async Task<AuditChainVerificationResult?> VerifyUpToEntryAsync(int entryId, CancellationToken ct = default)
    {
        var previousHash = string.Empty;
        var count = 0;

        await foreach (var entry in repo.StreamOrderedAsync().WithCancellation(ct).ConfigureAwait(false))
        {
            count++;

            if (entry.PreviousHash != previousHash)
            {
                return new AuditChainVerificationResult
                {
                    IsValid = false,
                    TotalEntries = count,
                    FirstInvalidId = entry.Id,
                    ErrorMessage = $"PreviousHash mismatch at entry {entry.Id}"
                };
            }

            if (entry.Hash != ComputeHash(entry, previousHash) &&
                entry.Hash != ComputeLegacyHash(entry, previousHash))
            {
                return new AuditChainVerificationResult
                {
                    IsValid = false,
                    TotalEntries = count,
                    FirstInvalidId = entry.Id,
                    ErrorMessage = $"Hash mismatch at entry {entry.Id}"
                };
            }

            // Reached the target with every preceding link intact: the entry is verified.
            if (entry.Id == entryId)
                return new AuditChainVerificationResult { IsValid = true, TotalEntries = count };

            previousHash = entry.Hash;
        }

        // No entry with that id in the chain.
        return null;
    }
}
