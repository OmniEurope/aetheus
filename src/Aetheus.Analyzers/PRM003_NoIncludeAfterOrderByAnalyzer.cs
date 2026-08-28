// SPDX-License-Identifier: EUPL-1.2
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Aetheus.Analyzers;

/// <summary>
/// PRM003: Forbids .Include() after .OrderBy/.OrderByDescending/.Skip/.Take in EF queries.
/// Include must come before ordering/pagination to avoid query plan issues.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PRM003_NoIncludeAfterOrderByAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "PRM003";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Include() must precede OrderBy/Skip/Take",
        "Move .Include() before .{0}() in the query chain",
        "Aetheus.EFConventions",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "EF Include after OrderBy/Skip/Take can produce suboptimal query plans.");

    private static readonly ImmutableHashSet<string> OrderingMethods =
        ["OrderBy", "OrderByDescending", "ThenBy", "ThenByDescending", "Skip", "Take"];

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess) return;
        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol
            is not IMethodSymbol method
            || method.Name is not ("Include" or "ThenInclude"))
        {
            return;
        }

        var declaredMethod = method.ReducedFrom ?? method;
        if (declaredMethod.ContainingType.ToDisplayString()
            != "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions")
        {
            return;
        }

        ExpressionSyntax current = method.ReducedFrom is not null
            ? memberAccess.Expression
            : invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression ?? memberAccess.Expression;
        if (TryFindOrderingMethod(
                current,
                invocation.SpanStart,
                context,
                new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default),
                out var orderingMethod))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Rule,
                memberAccess.Name.GetLocation(),
                orderingMethod));
        }
    }

    private static bool TryFindOrderingMethod(
        ExpressionSyntax expression,
        int beforePosition,
        SyntaxNodeAnalysisContext context,
        HashSet<ILocalSymbol> visited,
        out string orderingMethod)
    {
        var current = expression;
        while (current is InvocationExpressionSyntax invocation
               && invocation.Expression is MemberAccessExpressionSyntax member)
        {
            var method = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol
                as IMethodSymbol;
            var declared = method?.ReducedFrom ?? method;
            if (declared is not null
                && OrderingMethods.Contains(declared.Name)
                && declared.ContainingType.ToDisplayString()
                    is "System.Linq.Queryable" or "System.Linq.Enumerable")
            {
                orderingMethod = declared.Name;
                return true;
            }
            current = member.Expression;
        }

        if (current is IdentifierNameSyntax identifier
            && context.SemanticModel.GetSymbolInfo(identifier, context.CancellationToken).Symbol
                is ILocalSymbol local
            && visited.Add(local))
        {
            var assignment = identifier.SyntaxTree.GetRoot(context.CancellationToken)
                .DescendantNodes()
                .OfType<AssignmentExpressionSyntax>()
                .Where(candidate => candidate.SpanStart < beforePosition)
                .Where(candidate => SymbolEqualityComparer.Default.Equals(
                    context.SemanticModel.GetSymbolInfo(candidate.Left, context.CancellationToken).Symbol,
                    local))
                .OrderByDescending(candidate => candidate.SpanStart)
                .FirstOrDefault();
            if (assignment is not null)
            {
                return TryFindOrderingMethod(
                    assignment.Right,
                    assignment.SpanStart,
                    context,
                    visited,
                    out orderingMethod);
            }

            var declarator = local.DeclaringSyntaxReferences
                .Select(reference => reference.GetSyntax(context.CancellationToken))
                .OfType<VariableDeclaratorSyntax>()
                .FirstOrDefault();
            if (declarator?.Initializer is not null)
            {
                return TryFindOrderingMethod(
                    declarator.Initializer.Value,
                    declarator.SpanStart,
                    context,
                    visited,
                    out orderingMethod);
            }
        }

        orderingMethod = string.Empty;
        return false;
    }
}
