// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.Notifications;

internal static class NotificationConfigurationProtection
{
    internal static string Protect(IEncryptionService encryption, string configurationJson)
    {
        ArgumentNullException.ThrowIfNull(encryption);
        return encryption.EncryptValue(string.IsNullOrWhiteSpace(configurationJson) ? "{}" : configurationJson);
    }

    internal static string Unprotect(IEncryptionService encryption, string stored)
    {
        ArgumentNullException.ThrowIfNull(encryption);
        if (string.IsNullOrWhiteSpace(stored)) return "{}";

        var trimmed = stored.AsSpan().TrimStart();
        return trimmed.StartsWith("{".AsSpan(), StringComparison.Ordinal)
            || trimmed.StartsWith("[".AsSpan(), StringComparison.Ordinal)
            ? stored
            : encryption.DecryptValue(stored);
    }
}
