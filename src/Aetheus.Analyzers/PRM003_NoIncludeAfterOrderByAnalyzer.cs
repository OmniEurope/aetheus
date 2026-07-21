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

        // Walk up the fluent chain to see if any ordering/pagination method precedes this Include
        ExpressionSyntax current = method.ReducedFrom is not null
            ? memberAccess.Expression
            : invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression ?? memberAccess.Expression;
        while (current is InvocationExpressionSyntax parentInvocation &&
               parentInvocation.Expression is MemberAccessExpressionSyntax parentMember)
        {
            var parentMethod = context.SemanticModel.GetSymbolInfo(parentInvocation, context.CancellationToken).Symbol
                as IMethodSymbol;
            var declaredParent = parentMethod?.ReducedFrom ?? parentMethod;
            var parentName = declaredParent?.Name;
            var containingType = declaredParent?.ContainingType.ToDisplayString();
            if (parentName is not null
                && OrderingMethods.Contains(parentName)
                && containingType is "System.Linq.Queryable" or "System.Linq.Enumerable")
            {
                var diagnostic = Diagnostic.Create(Rule, memberAccess.Name.GetLocation(), parentName);
                context.ReportDiagnostic(diagnostic);
                return;
            }
            current = parentMember.Expression;
        }
    }
}
