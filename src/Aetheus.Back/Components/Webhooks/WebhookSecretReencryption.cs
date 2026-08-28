// SPDX-License-Identifier: EUPL-1.2
using System.Security.Cryptography;

namespace Aetheus.Back.Components.Webhooks;

/// <summary>
/// One-shot startup migration (audit 360 lot A). Converts any residual plaintext
/// <see cref="Data.Entities.WebhookSubscription.Secret"/> - written before webhook secrets were
/// encrypted at rest - into AES-256-GCM ciphertext via a read → re-encrypt → save round trip.
/// Idempotent: values that already decrypt cleanly are left untouched, so it is safe to run on every
/// startup. Once it has run against an environment, <see cref="WebhookService"/> can decrypt strictly
/// (no plaintext fallback), which is the whole point of the hardening.
/// </summary>
public static class WebhookSecretReencryption
{
    // AES-256-GCM at-rest layout (see EncryptionService): [version=0x01|0x02][nonce(12)][tag(16)][cipher].
    private const byte GcmVersionV1 = 0x01;
    private const byte GcmVersionV2 = 0x02;
    private const int GcmMinLength = 1 + 12 + 16; // version + nonce + tag

    public static async Task RunAsync(AppDbContext db, IEncryptionService encryption, ILogger logger, CancellationToken ct = default)
    {
        // Tracked query (no AsNoTracking) so the re-encrypted values persist on SaveChanges.
        var subscriptions = await db.WebhookSubscriptions
            .Where(w => w.Secret != null && w.Secret != "")
            .ToListAsync(ct).ConfigureAwait(false);

        var reencrypted = 0;
        foreach (var sub in subscriptions)
        {
            if (IsGcmCiphertext(sub.Secret!)) continue; // already AES-256-GCM at rest - leave untouched
            // Not GCM: a residual plaintext value, or (during a migration window) a legacy AES-CBC value.
            // Decrypt the legacy ciphertext if we can; otherwise treat the stored value as plaintext.
            // Either way the result is normalized to GCM.
            sub.Secret = encryption.EncryptValue(ResolvePlaintext(encryption, sub.Secret!));
            reencrypted++;
        }

        if (reencrypted > 0)
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            logger.LogInformation("Re-encrypted {Count} residual plaintext webhook secret(s) at rest.", reencrypted);
        }
    }

    /// <summary>
    /// Classifies a value as already-encrypted-at-rest by its <b>version-byte prefix</b> rather than by a
    /// trial decryption. The previous "does it decrypt?" probe mis-classified a residual plaintext value
    /// that happened to be valid base64 of sufficient length as ciphertext under <c>Auth:AllowLegacyCbc=true</c>
    /// (the legacy-CBC branch would "succeed" on it), leaving it un-re-encrypted. A GCM payload always starts
    /// with the 0x01/0x02 version byte, so the prefix check is unambiguous and never touches the cipher.
    /// </summary>
    private static bool IsGcmCiphertext(string stored)
    {
        byte[] raw;
        try { raw = Convert.FromBase64String(stored); }
        catch (FormatException) { return false; } // not base64 → plaintext
        return raw.Length >= GcmMinLength && (raw[0] == GcmVersionV1 || raw[0] == GcmVersionV2);
    }

    /// <summary>
    /// Returns the plaintext to re-encrypt for a non-GCM value: the decrypted legacy-CBC plaintext when the
    /// migration window allows it and the value really is legacy ciphertext, otherwise the stored value
    /// itself (a residual plaintext). Decryption failures (rejected legacy CBC, bad padding, non-base64)
    /// all fall back to treating the value as plaintext.
    /// </summary>
    private static string ResolvePlaintext(IEncryptionService encryption, string stored)
    {
        try
        {
            return encryption.DecryptValue(stored);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return stored;
        }
    }
}
