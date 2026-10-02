// SPDX-License-Identifier: EUPL-1.2
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Aetheus.Analyzers;

/// <summary>
/// SEC009: MD5, SHA-1 and <c>System.Random</c> do not produce or protect a secret.
///
/// <c>System.Random</c> is predictable from a few outputs, so a token, salt or nonce drawn from it can
/// be guessed. MD5 and SHA-1 are broken for collision resistance and far too fast for a password: a
/// hash of either stored as a verifier is recovered offline. The replacements are
/// <c>RandomNumberGenerator</c>, SHA-256 or HMAC-SHA-256, and a password hash (PBKDF2, BCrypt).
///
/// The primitives themselves are not reported: SHA-1 is how Git names its objects, and <c>Random</c>
/// is the right tool for retry jitter. Only a use in a secret context is, decided by
/// <see cref="SecretContext"/>: the value flows into a local, field, property or parameter whose name
/// has a secret word (token, secret, password, salt, nonce, otp, credential, a qualified key), or the
/// enclosing method or property is named that way. The .NET rules CA5350/CA5351/CA5394 report every
/// use; this one reports the ones that matter and can therefore be kept at warning.
///
/// Known false positive: a weak primitive used for a harmless purpose inside a method whose name has
/// a secret word, e.g. <c>Random.Shared</c> for retry jitter inside <c>RefreshTokenAsync</c>. Known
/// false negative: a secret held in a badly named variable (<c>var key = ...</c>, <c>var buffer</c>).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SEC009_NoWeakPrimitiveForASecretAnalyzer : ObjectCreationAnalyzer
{
    public const string DiagnosticId = "SEC009";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Do not use MD5, SHA-1 or System.Random for a secret",
        "'{0}' is used for '{1}'; use RandomNumberGenerator, SHA-256/HMAC-SHA-256 or a password hash instead",
        "Aetheus.Security",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "System.Random is predictable and MD5/SHA-1 are broken and too fast for passwords. Known false positive: a harmless use (jitter, a non-secret fingerprint) inside a method whose name mentions a secret.");

    private static readonly string[] WeakTypes =
    [
        "System.Security.Cryptography.MD5",
        "System.Security.Cryptography.SHA1",
        "System.Security.Cryptography.MD5CryptoServiceProvider",
        "System.Security.Cryptography.SHA1CryptoServiceProvider",
        "System.Security.Cryptography.SHA1Managed",
        "System.Random"
    ];

    protected override DiagnosticDescriptor Descriptor => Rule;

    protected override void RegisterFurtherActions(AnalysisContext context) =>
        context.RegisterSyntaxNodeAction(AnalyzeMemberAccess, SyntaxKind.SimpleMemberAccessExpression);

    protected override void AnalyzeCreation(SyntaxNodeAnalysisContext context)
    {
        var type = context.SemanticModel.GetTypeInfo(context.Node, context.CancellationToken).Type;
        var weak = WeakTypeName(type);
        if (weak is not null) ReportInSecretContext(context.Node, weak, context);
    }

    /// <summary>
    /// A static member of a weak type: <c>MD5.Create()</c>, <c>SHA1.HashData(...)</c>,
    /// <c>Random.Shared</c>. Instance members are not re-reported: the instance was reported where it
    /// was obtained.
    /// </summary>
    private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
    {
        var member = (MemberAccessExpressionSyntax)context.Node;
        if (context.SemanticModel.GetSymbolInfo(member, context.CancellationToken).Symbol
            is not { IsStatic: true, ContainingType: var type } symbol
            || symbol.Kind is not (SymbolKind.Method or SymbolKind.Property))
        {
            return;
        }
        var weak = WeakTypeName(type);
        if (weak is null) return;
        // Report the outermost call, so `SHA1.HashData(x)` and `Random.Shared.Next()` flow into
        // whatever they are assigned to rather than stopping at their own member access.
        SyntaxNode reported = member.Parent is InvocationExpressionSyntax invocation ? invocation : member;
        ReportInSecretContext(reported, weak, context);
    }

    private static void ReportInSecretContext(SyntaxNode node, string weak, SyntaxNodeAnalysisContext context)
    {
        var secretName = SecretContext.FindSecretName(node, context);
        if (secretName is null) return;
        context.ReportDiagnostic(Diagnostic.Create(Rule, node.GetLocation(), weak, secretName));
    }

    private static string? WeakTypeName(ITypeSymbol? type)
    {
        if (type is null) return null;
        var name = type.ToDisplayString();
        return System.Array.IndexOf(WeakTypes, name) >= 0 ? type.Name : null;
    }
}
