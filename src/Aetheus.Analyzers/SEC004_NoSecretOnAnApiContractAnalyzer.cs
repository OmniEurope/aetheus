// SPDX-License-Identifier: EUPL-1.2
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Aetheus.Analyzers;

/// <summary>
/// SEC004: a type in the shared DTO contract must not carry a readable secret.
///
/// A DTO is what crosses the wire, so a property named <c>Password</c> or <c>ApiKey</c> on one is a
/// disclosure by construction, whether or not any endpoint currently returns it: the next endpoint
/// that projects the whole record discloses it without anybody editing the property.
///
/// Write-only inputs are the legitimate exception and are allowed: a login request has to carry a
/// password, and a create-vault-entry request has to carry a value. What the rule refuses is a
/// secret a caller can READ back, which is why it looks at the getter rather than at the name alone.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SEC004_NoSecretOnAnApiContractAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "SEC004";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "An API contract must not expose a readable secret",
        "'{0}.{1}' is readable on a wire contract and names a secret; return an identifier or a masked form",
        "Aetheus.Security",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A DTO is what crosses the wire. A readable secret on one is disclosed by the first endpoint that projects the whole record, without anybody editing the property.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeProperty, SyntaxKind.PropertyDeclaration);
    }

    private static void AnalyzeProperty(SyntaxNodeAnalysisContext context)
    {
        var declaration = (PropertyDeclarationSyntax)context.Node;
        if (context.SemanticModel.GetDeclaredSymbol(declaration, context.CancellationToken)
            is not IPropertySymbol property)
        {
            return;
        }
        if (property.DeclaredAccessibility != Accessibility.Public) return;
        // Write-only is the legitimate shape for an inbound secret: a login request carries a
        // password precisely so the caller can send one it can never read back.
        if (property.GetMethod is null) return;
        if (!IsWireContract(property.ContainingType)) return;
        if (!IsSecretValued(property.Type)) return;
        if (!NamesASecret(property.Name)) return;

        context.ReportDiagnostic(Diagnostic.Create(
            Rule, declaration.Identifier.GetLocation(), property.ContainingType.Name, property.Name));
    }

    /// <summary>
    /// A type that exists to cross the wire. Scoped by namespace and by name rather than by "is it
    /// returned somewhere", because the point is that a contract must be safe before anybody returns
    /// it: a rule that waited for a use site would only fire once the disclosure already existed.
    /// </summary>
    private static readonly string[] WireContractSuffixes = ["Dto", "Response", "Result", "Summary"];

    private static bool IsWireContract(INamedTypeSymbol type)
    {
        var name = type.Name;
        if (!EndsWithAny(name, WireContractSuffixes)) return false;
        // Requests are inbound and may legitimately carry a secret to be stored; a *Request that also
        // exposes it back is caught by the getter check above being combined with this exclusion.
        return !name.EndsWith("Request", System.StringComparison.Ordinal);
    }

    private static bool IsSecretValued(ITypeSymbol type)
    {
        if (type.SpecialType is SpecialType.System_String or SpecialType.System_Object) return true;
        return type is IArrayTypeSymbol array
            && array.ElementType.SpecialType is SpecialType.System_Char or SpecialType.System_Byte;
    }

    /// <summary>
    /// Deliberately narrower than SEC003's list. A DTO legitimately carries names, identifiers,
    /// versions and hashes of secrets, and those are what a client needs to work with a secret it
    /// must never see.
    /// </summary>
    /// <summary>What a client legitimately needs in order to work with a secret it never sees.</summary>
    private static readonly string[] AboutASecretSuffixes =
        ["name", "names", "id", "ids", "hash", "version", "keys", "count", "masked", "preview"];

    private static readonly string[] SecretSubstrings = ["password", "passphrase", "secret", "credential"];

    private static readonly string[] SecretSuffixes =
        ["token", "apikey", "privatekey", "ingestkey", "encryptionkey"];

    private static bool NamesASecret(string name)
    {
        var lowered = name.ToLowerInvariant();
        if (EndsWithAny(lowered, AboutASecretSuffixes)) return false;
        return System.Array.Exists(SecretSubstrings, part => lowered.Contains(part))
            || EndsWithAny(lowered, SecretSuffixes);
    }

    private static bool EndsWithAny(string value, string[] suffixes) =>
        System.Array.Exists(suffixes, suffix => value.EndsWith(suffix, System.StringComparison.Ordinal));
}
