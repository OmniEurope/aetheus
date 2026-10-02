// SPDX-License-Identifier: EUPL-1.2
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Aetheus.Analyzers;

/// <summary>
/// SEC006: an <c>HttpClient</c> is taken from <c>IHttpClientFactory</c> or a typed client, not built
/// with <c>new</c> wherever it is needed.
///
/// A client built per call owns its own connection pool: every call opens fresh sockets that then sit
/// in TIME_WAIT, and a busy path exhausts the ephemeral ports of the host it runs on. A client kept for
/// the life of the process without a pooled-connection lifetime keeps resolving the DNS answer it got
/// first. The factory solves both, which is why it is the default answer here.
///
/// Two shapes are not reported, because they are the documented alternatives:
/// - a construction in the initializer of a STATIC field or property (one instance for the process;
///   the caller owns the handler's connection lifetime);
/// - a construction inside a lambda handed to a <c>Microsoft.Extensions.DependencyInjection</c>
///   registration (<c>AddSingleton(sp =&gt; new HttpClient(...))</c>, typed-client configuration).
///
/// Known false positive: a one-shot process (a CLI command, an installer probe) that builds a single
/// client for its whole lifetime outside any DI container is reported, although it cannot exhaust
/// anything. Suppress it with a <c>SuppressMessage</c> that says so. Known false negative: a static
/// <c>Func&lt;HttpClient&gt;</c> initializer that builds a client on every call is not reported.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SEC006_HttpClientComesFromTheFactoryAnalyzer : ObjectCreationAnalyzer
{
    public const string DiagnosticId = "SEC006";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Take HttpClient from IHttpClientFactory or a typed client",
        "'{0}' is constructed directly; take it from IHttpClientFactory or a typed client, or keep one static instance",
        "Aetheus.Security",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A client built per call owns its own connection pool and exhausts the host's sockets under load; a long-lived one without a pooled-connection lifetime keeps a stale DNS answer. Known false positive: a one-shot process that builds a single client for its lifetime outside DI.");

    protected override DiagnosticDescriptor Descriptor => Rule;

    protected override void AnalyzeCreation(SyntaxNodeAnalysisContext context)
    {
        var creation = (BaseObjectCreationExpressionSyntax)context.Node;
        var type = context.SemanticModel.GetTypeInfo(creation, context.CancellationToken).Type;
        if (!HttpClientCreation.IsHttpClient(type)) return;
        if (IsInStaticInitializer(creation) || IsInDependencyInjectionRegistration(creation, context)) return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, creation.GetLocation(), type!.Name));
    }

    /// <summary>The initializer of a static field or property: one instance for the process.</summary>
    private static bool IsInStaticInitializer(SyntaxNode creation)
    {
        var initializer = creation.Ancestors().OfType<EqualsValueClauseSyntax>().FirstOrDefault();
        if (initializer is null) return false;
        var member = initializer.Ancestors().OfType<MemberDeclarationSyntax>().FirstOrDefault();
        return member is FieldDeclarationSyntax or PropertyDeclarationSyntax
            && member.Modifiers.Any(SyntaxKind.StaticKeyword);
    }

    /// <summary>A lambda argument of a DI registration: the container owns the lifetime.</summary>
    private static bool IsInDependencyInjectionRegistration(SyntaxNode creation, SyntaxNodeAnalysisContext context)
    {
        foreach (var lambda in creation.Ancestors().OfType<AnonymousFunctionExpressionSyntax>())
        {
            if (lambda.Parent is not ArgumentSyntax { Parent.Parent: InvocationExpressionSyntax invocation }) continue;
            if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol
                is IMethodSymbol method
                && (method.ContainingNamespace?.ToDisplayString() ?? string.Empty)
                    .StartsWith("Microsoft.Extensions.DependencyInjection", System.StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }
}
