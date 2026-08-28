// SPDX-License-Identifier: EUPL-1.2
using System.Security.Cryptography;
using System.Text;
using Aetheus.Back.Data.Entities;

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
        return (await VerifyAsync(targetEntryId: null, ct).ConfigureAwait(false))!;
    }

    public async Task<AuditChainVerificationResult?> VerifyUpToEntryAsync(int entryId, CancellationToken ct = default)
    {
        return await VerifyAsync(entryId, ct).ConfigureAwait(false);
    }

    private async Task<AuditChainVerificationResult?> VerifyAsync(int? targetEntryId, CancellationToken ct)
    {
        // F-006: streamed verification - bounded memory regardless of table size.
        var previousHash = string.Empty;
        var count = 0;

        await foreach (var entry in repo.StreamOrderedAsync().WithCancellation(ct).ConfigureAwait(false))
        {
            count++;

            var failure = ValidateEntry(entry, previousHash, count);
            if (failure is not null)
                return failure;

            // Reached the target with every preceding link intact: the entry is verified.
            if (entry.Id == targetEntryId)
                return new AuditChainVerificationResult { IsValid = true, TotalEntries = count };

            previousHash = entry.Hash;
        }

        return targetEntryId.HasValue
            ? null
            : new AuditChainVerificationResult { IsValid = true, TotalEntries = count };
    }

    private AuditChainVerificationResult? ValidateEntry(AuditLog entry, string previousHash, int count)
    {
        if (entry.PreviousHash != previousHash)
            return InvalidEntry(entry, count, "PreviousHash mismatch");

        if (entry.Hash != ComputeHash(entry, previousHash) &&
            entry.Hash != ComputeLegacyHash(entry, previousHash))
            return InvalidEntry(entry, count, "Hash mismatch");

        return null;
    }

    private static AuditChainVerificationResult InvalidEntry(AuditLog entry, int count, string reason) => new()
    {
        IsValid = false,
        TotalEntries = count,
        FirstInvalidId = entry.Id,
        ErrorMessage = $"{reason} at entry {entry.Id}"
    };
}
