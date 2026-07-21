// SPDX-License-Identifier: EUPL-1.2
using System.Text.Json;
using System.Text.Json.Serialization;
using Aetheus.Shared.Enums;

namespace Aetheus.Back.Components.ExternalRepos;

/// <summary>
/// Typed view over the encrypted JSON stored in <c>ServiceConnection.EncryptedPayload</c> for Git
/// connections. Never logged; the raw secrets (<see cref="Token"/>, <see cref="PrivateKeyPem"/>,
/// <see cref="Passphrase"/>) are masked by <c>SecretMaskingService</c> and encrypted at rest.
/// </summary>
public sealed record GitCredentialPayload
{
    [JsonPropertyName("authType")]
    public GitAuthType AuthType { get; init; }

    /// <summary>Username for HTTPS basic auth (often a literal like <c>x-access-token</c> or <c>oauth2</c>).</summary>
    [JsonPropertyName("username")]
    public string? Username { get; init; }

    /// <summary>Personal Access Token for HTTPS auth. Secret.</summary>
    [JsonPropertyName("token")]
    public string? Token { get; init; }

    /// <summary>PEM-encoded private key for SSH auth. Secret.</summary>
    [JsonPropertyName("privateKeyPem")]
    public string? PrivateKeyPem { get; init; }

    /// <summary>Optional passphrase protecting the SSH private key. Secret.</summary>
    [JsonPropertyName("passphrase")]
    public string? Passphrase { get; init; }

    /// <summary>
    /// Pinned <c>known_hosts</c> entry/entries for the SSH host. Required for SSH - we never
    /// fall back to <c>StrictHostKeyChecking=no</c>.
    /// </summary>
    [JsonPropertyName("knownHosts")]
    public string? KnownHosts { get; init; }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Parses a decrypted payload JSON into a typed credential. Returns <c>null</c> when the payload
    /// is empty or malformed (caller decides whether anonymous access is acceptable).
    /// </summary>
    public static GitCredentialPayload? TryParse(string? decryptedJson)
    {
        if (string.IsNullOrWhiteSpace(decryptedJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<GitCredentialPayload>(decryptedJson, SerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
