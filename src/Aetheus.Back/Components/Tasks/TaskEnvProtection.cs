// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Services;

namespace Aetheus.Back.Components.Tasks;

/// <summary>
/// F-001: <c>ServerTask.EnvironmentVariables</c> can carry vault secrets and is therefore
/// encrypted at rest (AES-256-GCM via <see cref="IEncryptionService"/>). Legacy rows written
/// before the cutover are plaintext JSON - a stored value starting with '{' is read as-is,
/// while encrypted payloads are base64 of a version-prefixed blob (never starts with '{').
/// </summary>
internal static class TaskEnvProtection
{
    internal const string EmptyEnv = "{}";

    internal static string Protect(IEncryptionService encryption, string envJson)
    {
        ArgumentNullException.ThrowIfNull(encryption);
        return string.IsNullOrWhiteSpace(envJson) || envJson == EmptyEnv
            ? EmptyEnv
            : encryption.EncryptValue(envJson);
    }

    internal static string Unprotect(IEncryptionService encryption, string stored)
    {
        ArgumentNullException.ThrowIfNull(encryption);
        return string.IsNullOrWhiteSpace(stored) || stored.StartsWith('{')
            ? stored
            : encryption.DecryptValue(stored);
    }
}
