// SPDX-License-Identifier: EUPL-1.2
using System.Security.Cryptography;
using System.Text;

namespace Aetheus.Back.Services;

/// <summary>
/// Validates webhook signatures sent by Git providers. Supports GitHub-style HMAC-SHA256
/// (`sha256=<hex>`) and GitLab-style plain-token comparison.
/// </summary>
public static class WebhookSignatureValidator
{
    public static bool Validate(string? signature, string secret, string rawBody)
    {
        if (string.IsNullOrEmpty(signature)) return false;

        if (!signature.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase))
            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(signature),
                Encoding.UTF8.GetBytes(secret));

        var expected = "sha256=" + Convert.ToHexStringLower(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(rawBody)));
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(signature),
            Encoding.UTF8.GetBytes(expected));
    }

    /// <summary>
    /// Computes a lowercase-hex HMAC-SHA256 signature of <paramref name="payload"/> using
    /// <paramref name="secret"/>. Used by webhook/notification services to sign outgoing payloads.
    /// </summary>
    public static string Compute(string secret, string payload)
    {
        var key = Encoding.UTF8.GetBytes(secret);
        var data = Encoding.UTF8.GetBytes(payload);
        var hash = HMACSHA256.HashData(key, data);
        return Convert.ToHexStringLower(hash);
    }
}
