// SPDX-License-Identifier: EUPL-1.2
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;

namespace Aetheus.Analyzers.Tests;

/// <summary>
/// SEC004 refuses a secret a caller can READ off a wire contract. Write-only inputs and the
/// identifiers, names and hashes a client needs in order to work with a secret it never sees are
/// exactly what must stay allowed.
/// </summary>
public sealed class Sec004Tests
{
    private static Task VerifyAsync(string source, params DiagnosticResult[] expected) =>
        Verifier<SEC004_NoSecretOnAnApiContractAnalyzer>.VerifyAsync("Contracts.cs", source, expected);

    private static DiagnosticResult At(int line, int column, int endColumn, string type, string property) =>
        Verifier<SEC004_NoSecretOnAnApiContractAnalyzer>.Expect(
            "SEC004", DiagnosticSeverity.Warning, "Contracts.cs", line, column, line, endColumn, type, property);

    [Fact]
    public async Task AReadablePasswordOnADtoIsReported()
    {
        await VerifyAsync("""
            public sealed record VaultEntryDto
            {
                public string Password { get; init; } = string.Empty;
            }
            """,
            At(3, 19, 27, "VaultEntryDto", "Password"));
    }

    [Fact]
    public async Task AReadableApiKeyOnAResponseIsReported()
    {
        await VerifyAsync("""
            public sealed record ServerResponse
            {
                public string IngestApiKey { get; set; } = string.Empty;
            }
            """,
            At(3, 19, 31, "ServerResponse", "IngestApiKey"));
    }

    [Fact]
    public async Task AWriteOnlySecretIsAccepted()
    {
        // A login request has to carry a password; what it must not do is hand it back.
        await VerifyAsync("""
            public sealed class VaultEntryDto
            {
                private string _password = string.Empty;
                public string Password { set => _password = value; }
            }
            """);
    }

    [Fact]
    public async Task AnIdentifierOrNameIsAccepted()
    {
        await VerifyAsync("""
            public sealed record VaultEntryDto
            {
                public string SecretName { get; init; } = string.Empty;
                public int TokenId { get; init; }
                public string PasswordHash { get; init; } = string.Empty;
                public string ApiKeyMasked { get; init; } = string.Empty;
            }
            """);
    }

    [Fact]
    public async Task ARequestTypeIsNotAReadContract()
    {
        await VerifyAsync("""
            public sealed record LoginRequest
            {
                public string Password { get; init; } = string.Empty;
            }
            """);
    }

    [Fact]
    public async Task ATypeThatIsNotAWireContractIsNotReported()
    {
        // An entity or a service holds secrets by design; this rule is about what crosses the wire.
        await VerifyAsync("""
            public sealed class VaultEntry
            {
                public string Password { get; set; } = string.Empty;
            }
            """);
    }

    [Fact]
    public async Task ANonSecretValuedPropertyIsNotReported()
    {
        await VerifyAsync("""
            public sealed record VaultEntryDto
            {
                public bool HasPassword { get; init; }
                public int SecretCount { get; init; }
            }
            """);
    }
}
