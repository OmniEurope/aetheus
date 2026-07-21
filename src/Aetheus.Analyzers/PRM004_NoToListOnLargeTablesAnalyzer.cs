// SPDX-License-Identifier: EUPL-1.2
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Aetheus.Analyzers;

/// <summary>
/// PRM004: Warns on .ToListAsync() / .ToList() calls in repositories when the query
/// does not have a bounding guard earlier in the chain, suggesting unbounded
/// materialization risk. A filter narrows a result set but does not impose an upper
/// bound, so only an explicit Take is considered a bounding guard.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PRM004_NoToListOnLargeTablesAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "PRM004";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Unbounded materialization without Take",
        "Consider adding .Take() before .{0}() to avoid unbounded materialization",
        "Aetheus.Performance",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Materialization without a Take guard may load an unbounded result set into memory.");

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
            is not IMethodSymbol method)
        {
            return;
        }

        var declaredMethod = method.ReducedFrom ?? method;
        var methodName = declaredMethod.Name;
        if (methodName is not ("ToListAsync" or "ToList" or "ToArrayAsync" or "ToArray")) return;

        ExpressionSyntax source = method.ReducedFrom is not null
            ? memberAccess.Expression
            : invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression ?? memberAccess.Expression;
        if (!IsEfMaterialization(declaredMethod, source, context)) return;

        // Only flag in Repository files: the type-name segment ends with "Repository"
        // (e.g. XxxRepository.cs AND partial files XxxRepository.Area.cs - the leading
        // dot-segment is the class name). A file merely *containing* "Repository"
        // (RepositoryUrlNormalizer.cs, RepositoryRegistration.cs) is not a repository.
        var filePath = invocation.SyntaxTree.FilePath;
        var fileName = System.IO.Path.GetFileNameWithoutExtension(filePath);
        var typeSegment = fileName.Split('.')[0];
        if (!typeSegment.EndsWith("Repository", System.StringComparison.Ordinal)) return;

        // Walk up the chain to check for an explicit upper bound.
        var current = source;
        while (current is InvocationExpressionSyntax parentInvocation &&
               parentInvocation.Expression is MemberAccessExpressionSyntax parentMember)
        {
            var parentMethod = context.SemanticModel.GetSymbolInfo(parentInvocation, context.CancellationToken).Symbol
                as IMethodSymbol;
            var declaredParent = parentMethod?.ReducedFrom ?? parentMethod;
            if (declaredParent?.Name == "Take"
                && declaredParent.ContainingType.ToDisplayString() is "System.Linq.Queryable" or "System.Linq.Enumerable")
                return; // Guarded - no diagnostic
            current = parentMember.Expression;
        }

        var diagnostic = Diagnostic.Create(Rule, memberAccess.Name.GetLocation(), methodName);
        context.ReportDiagnostic(diagnostic);
    }

    private static bool IsEfMaterialization(
        IMethodSymbol method,
        ExpressionSyntax source,
        SyntaxNodeAnalysisContext context)
    {
        var containingType = method.ContainingType.ToDisplayString();
        if (method.Name is "ToListAsync" or "ToArrayAsync")
            return containingType == "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions";

        if (containingType != "System.Linq.Enumerable") return false;
        var sourceType = context.SemanticModel.GetTypeInfo(source, context.CancellationToken).Type;
        return sourceType is not null && (sourceType.OriginalDefinition.ToDisplayString() == "System.Linq.IQueryable<T>"
            || sourceType.AllInterfaces.Any(type => type.OriginalDefinition.ToDisplayString() == "System.Linq.IQueryable<T>"));
    }
}
