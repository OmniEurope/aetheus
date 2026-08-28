// SPDX-License-Identifier: EUPL-1.2
using System.Security.Cryptography;
using System.Text;

namespace Aetheus.Back.Components.Auth;

/// <summary>
/// Shared token primitives used by <see cref="AuthService"/> and <see cref="ServerEnrollmentService"/>.
/// Extracted to a single helper so the HMAC keying is identical across both - and so neither service
/// needs a <c>partial</c> split to share it.
/// </summary>
internal static class AuthTokenHelper
{
    public static string GenerateSecureToken()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public static string HashToken(string signingKey, string token)
    {
        var key = Encoding.UTF8.GetBytes(signingKey);
        var bytes = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(bytes);
    }

    public static string PadOrTrim(string input, int targetLength)
    {
        input ??= string.Empty;
        return input.Length == targetLength ? input : input.PadRight(targetLength, '\0');
    }
}
