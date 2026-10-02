// SPDX-License-Identifier: EUPL-1.2
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Aetheus.Analyzers;

/// <summary>
/// SEC011: <c>BinaryFormatter</c> is not used at all.
///
/// Deserializing with it runs code chosen by whoever wrote the bytes; Microsoft's own guidance is that
/// it cannot be made safe, and from .NET 9 the runtime throws on it. A use in this codebase is either
/// dead code that a future runtime switch would revive, or a real remote code execution path. Both
/// are reported: every construction and every member access on the type.
///
/// Known false positive: a deliberate construction whose only purpose is to prove the runtime refuses
/// it (a startup self-check that expects <c>PlatformNotSupportedException</c>) is reported. Known
/// false negative: an instance obtained through reflection or <c>Activator.CreateInstance</c>.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SEC011_NoBinaryFormatterAnalyzer : ObjectCreationAnalyzer
{
    public const string DiagnosticId = "SEC011";

    private const string BinaryFormatterType = "System.Runtime.Serialization.Formatters.Binary.BinaryFormatter";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Do not use BinaryFormatter",
        "'{0}' uses BinaryFormatter, which executes whatever the serialized bytes describe; use System.Text.Json",
        "Aetheus.Security",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "BinaryFormatter deserialization is a remote code execution primitive and cannot be made safe. Known false positive: a construction that only proves the runtime refuses it.");

    protected override DiagnosticDescriptor Descriptor => Rule;

    protected override void RegisterFurtherActions(AnalysisContext context) =>
        context.RegisterSyntaxNodeAction(AnalyzeMemberAccess, SyntaxKind.SimpleMemberAccessExpression);

    protected override void AnalyzeCreation(SyntaxNodeAnalysisContext context)
    {
        var type = context.SemanticModel.GetTypeInfo(context.Node, context.CancellationToken).Type;
        if (IsBinaryFormatter(type))
            context.ReportDiagnostic(Diagnostic.Create(Rule, context.Node.GetLocation(), "new BinaryFormatter"));
    }

    private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
    {
        var member = (MemberAccessExpressionSyntax)context.Node;
        var symbol = context.SemanticModel.GetSymbolInfo(member, context.CancellationToken).Symbol;
        if (symbol is IMethodSymbol or IPropertySymbol && IsBinaryFormatter(symbol.ContainingType))
            context.ReportDiagnostic(Diagnostic.Create(Rule, member.GetLocation(), member.ToString()));
    }

    private static bool IsBinaryFormatter(ITypeSymbol? type) => type?.ToDisplayString() == BinaryFormatterType;
}
