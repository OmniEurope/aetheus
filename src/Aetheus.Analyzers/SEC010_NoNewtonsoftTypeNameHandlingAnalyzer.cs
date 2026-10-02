// SPDX-License-Identifier: EUPL-1.2
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Aetheus.Analyzers;

/// <summary>
/// SEC010: Newtonsoft.Json <c>TypeNameHandling</c> stays <c>None</c>.
///
/// Any other value makes the serializer honour a <c>$type</c> property found in the payload and
/// instantiate whatever type it names. A caller that controls the JSON then chooses the class and its
/// property setters, which is the textbook gadget-chain remote code execution. There is no binder
/// strict enough to make that safe on untrusted input, and Aetheus reads JSON from agents, webhooks
/// and pipeline definitions.
///
/// Reported: every use of a <c>TypeNameHandling</c> member other than <c>None</c> (an assignment,
/// an initializer, an attribute argument, a method argument), and a cast of a non-zero constant to it.
/// Not reported: a comparison (<c>==</c>, <c>!=</c>, a pattern or a <c>case</c> label), which is how a
/// guard that refuses the setting is written.
///
/// Known false positive: a non-None value passed to a method that REJECTS it (a validator that takes
/// the value as an argument) is reported, because an argument is also how the setting is applied.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SEC010_NoNewtonsoftTypeNameHandlingAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "SEC010";

    private const string TypeNameHandlingType = "Newtonsoft.Json.TypeNameHandling";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Keep Newtonsoft.Json TypeNameHandling at None",
        "TypeNameHandling '{0}' lets the payload choose the type that is instantiated; keep it at None",
        "Aetheus.Security",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A $type honoured from untrusted JSON is a remote code execution primitive. Known false positive: a non-None value passed as an argument to a validator that rejects it.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeMemberAccess, SyntaxKind.SimpleMemberAccessExpression);
        context.RegisterSyntaxNodeAction(AnalyzeCast, SyntaxKind.CastExpression);
    }

    private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
    {
        var member = (MemberAccessExpressionSyntax)context.Node;
        if (context.SemanticModel.GetSymbolInfo(member, context.CancellationToken).Symbol
            is not IFieldSymbol { ContainingType: var type, Name: var name }
            || type.ToDisplayString() != TypeNameHandlingType
            || name == "None"
            || IsComparison(member))
        {
            return;
        }
        context.ReportDiagnostic(Diagnostic.Create(Rule, member.GetLocation(), name));
    }

    private static void AnalyzeCast(SyntaxNodeAnalysisContext context)
    {
        var cast = (CastExpressionSyntax)context.Node;
        var type = context.SemanticModel.GetTypeInfo(cast, context.CancellationToken).Type;
        if (type?.ToDisplayString() != TypeNameHandlingType || IsComparison(cast)) return;
        var constant = context.SemanticModel.GetConstantValue(cast.Expression, context.CancellationToken);
        if (!constant.HasValue || System.Convert.ToInt64(constant.Value, System.Globalization.CultureInfo.InvariantCulture) == 0) return;
        context.ReportDiagnostic(Diagnostic.Create(Rule, cast.GetLocation(), constant.Value!.ToString()));
    }

    private static bool IsComparison(SyntaxNode node)
    {
        var parent = node.Parent;
        while (parent is ParenthesizedExpressionSyntax) parent = parent.Parent;
        return parent is ConstantPatternSyntax or CaseSwitchLabelSyntax
            || parent is BinaryExpressionSyntax binary
                && (binary.IsKind(SyntaxKind.EqualsExpression) || binary.IsKind(SyntaxKind.NotEqualsExpression));
    }
}
