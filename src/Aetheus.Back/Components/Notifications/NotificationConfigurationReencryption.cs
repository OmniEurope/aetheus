// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.Notifications;

/// <summary>One-shot, idempotent startup migration for notification configurations that predate
/// encryption at rest.</summary>
public static class NotificationConfigurationReencryption
{
    public static async Task RunAsync(
        AppDbContext db,
        IEncryptionService encryption,
        ILogger logger,
        CancellationToken ct = default)
    {
        var channels = await db.NotificationChannels.ToListAsync(ct).ConfigureAwait(false);
        var migrated = 0;
        foreach (var channel in channels)
        {
            var stored = channel.ConfigurationJson;
            if (!LooksLikePlainJson(stored)) continue;

            channel.ConfigurationJson = NotificationConfigurationProtection.Protect(encryption, stored);
            migrated++;
        }

        if (migrated == 0) return;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        logger.LogInformation(
            "Encrypted {Count} residual plaintext notification configuration(s) at rest.",
            migrated);
    }

    private static bool LooksLikePlainJson(string stored)
    {
        if (string.IsNullOrWhiteSpace(stored)) return true;
        var trimmed = stored.AsSpan().TrimStart();
        return trimmed.StartsWith("{".AsSpan(), StringComparison.Ordinal)
            || trimmed.StartsWith("[".AsSpan(), StringComparison.Ordinal);
    }
}
