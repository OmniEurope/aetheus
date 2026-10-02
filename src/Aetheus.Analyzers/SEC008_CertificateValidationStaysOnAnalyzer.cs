// SPDX-License-Identifier: EUPL-1.2
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Aetheus.Analyzers;

/// <summary>
/// SEC008: TLS certificate validation is not switched off.
///
/// Reported: a lambda or anonymous method used as a certificate-validation callback (any delegate
/// that receives an <c>SslPolicyErrors</c> and returns <c>bool</c>: <c>RemoteCertificateValidationCallback</c>,
/// <c>HttpClientHandler.ServerCertificateCustomValidationCallback</c>,
/// <c>ServicePointManager.ServerCertificateValidationCallback</c>) whose body returns <c>true</c>
/// unconditionally, and any use of <c>HttpClientHandler.DangerousAcceptAnyServerCertificateValidator</c>.
/// Either one turns the channel into one that any machine on the path can read and rewrite, and in
/// Aetheus that channel carries agent tokens, deployment credentials and vault secrets.
///
/// The .NET rule CA5359 covers part of this; this one also covers the handler's named validator and
/// the <c>Func</c>-typed callback, and it belongs to the project's own security surface.
///
/// Known false positive: an accept-any callback that is only reachable behind a guard the rule cannot
/// see (a loopback-only development switch refused at startup elsewhere) is reported. Suppress it with
/// a <c>SuppressMessage</c> that names the guard. Known false negative: a callback that ignores its
/// <c>SslPolicyErrors</c> without literally returning <c>true</c> (<c>return errors == errors;</c>).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SEC008_CertificateValidationStaysOnAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "SEC008";

    private const string SslPolicyErrorsType = "System.Net.Security.SslPolicyErrors";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Do not disable TLS certificate validation",
        "{0} accepts any server certificate; validate the chain or pin the expected thumbprint",
        "Aetheus.Security",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A validation callback that always returns true lets anyone on the network path impersonate the server. Known false positive: an accept-any callback reachable only behind a guard enforced elsewhere.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(
            AnalyzeCallback,
            SyntaxKind.SimpleLambdaExpression,
            SyntaxKind.ParenthesizedLambdaExpression,
            SyntaxKind.AnonymousMethodExpression);
        context.RegisterSyntaxNodeAction(AnalyzeMemberAccess, SyntaxKind.SimpleMemberAccessExpression);
    }

    private static void AnalyzeCallback(SyntaxNodeAnalysisContext context)
    {
        var function = (AnonymousFunctionExpressionSyntax)context.Node;
        if (!AlwaysReturnsTrue(function)) return;
        var delegateType = context.SemanticModel.GetTypeInfo(function, context.CancellationToken).ConvertedType
            as INamedTypeSymbol;
        if (!IsCertificateCallback(delegateType)) return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, function.GetLocation(), "This validation callback"));
    }

    private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
    {
        var member = (MemberAccessExpressionSyntax)context.Node;
        if (member.Name.Identifier.Text != "DangerousAcceptAnyServerCertificateValidator") return;
        if (context.SemanticModel.GetSymbolInfo(member, context.CancellationToken).Symbol
            is not IPropertySymbol { ContainingType: var type }
            || type.ToDisplayString() != "System.Net.Http.HttpClientHandler")
        {
            return;
        }
        context.ReportDiagnostic(Diagnostic.Create(
            Rule, member.GetLocation(), "HttpClientHandler.DangerousAcceptAnyServerCertificateValidator"));
    }

    /// <summary>`=&gt; true`, or a block whose only statement is `return true;`.</summary>
    private static bool AlwaysReturnsTrue(AnonymousFunctionExpressionSyntax function) => function.Body switch
    {
        LiteralExpressionSyntax literal => literal.IsKind(SyntaxKind.TrueLiteralExpression),
        BlockSyntax { Statements.Count: 1 } block =>
            block.Statements[0] is ReturnStatementSyntax { Expression: LiteralExpressionSyntax literal }
            && literal.IsKind(SyntaxKind.TrueLiteralExpression),
        _ => false
    };

    /// <summary>A delegate that is handed the TLS policy errors and answers whether to proceed.</summary>
    private static bool IsCertificateCallback(INamedTypeSymbol? delegateType)
    {
        var invoke = delegateType?.DelegateInvokeMethod;
        return invoke is { ReturnType.SpecialType: SpecialType.System_Boolean }
            && invoke.Parameters.Any(parameter => parameter.Type.ToDisplayString() == SslPolicyErrorsType);
    }
}
