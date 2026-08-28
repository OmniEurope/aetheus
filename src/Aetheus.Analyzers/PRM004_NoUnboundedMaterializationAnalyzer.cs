// SPDX-License-Identifier: EUPL-1.2
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Aetheus.Analyzers;

/// <summary>
/// PRM004: reports unbounded collection materialization from an EF/IQueryable source inside a
/// repository type. A filter narrows a result set but does not impose an upper bound; only Take
/// is considered a bounding guard.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PRM004_NoUnboundedMaterializationAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "PRM004";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Unbounded query materialization",
        "Consider adding .Take() before .{0}() to avoid unbounded materialization",
        "Aetheus.Performance",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Collection materialization from an IQueryable without a Take guard may load an unbounded result set into memory.");

    private static readonly ImmutableHashSet<string> MaterializationMethods =
    [
        "ToListAsync", "ToArrayAsync", "ToDictionaryAsync", "ToHashSetAsync",
        "ToList", "ToArray", "ToDictionary", "ToHashSet", "ToLookup"
    ];

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
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess
            || context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol
                is not IMethodSymbol method)
        {
            return;
        }

        var declaredMethod = method.ReducedFrom ?? method;
        if (!MaterializationMethods.Contains(declaredMethod.Name)) return;

        ExpressionSyntax source = method.ReducedFrom is not null
            ? memberAccess.Expression
            : invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression ?? memberAccess.Expression;
        if (!IsQueryMaterialization(declaredMethod, source, context)) return;
        if (!IsInsideRepositoryType(invocation, context)) return;
        if (HasTakeBound(source, context, new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default))) return;

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            memberAccess.Name.GetLocation(),
            declaredMethod.Name));
    }

    private static bool IsInsideRepositoryType(
        InvocationExpressionSyntax invocation,
        SyntaxNodeAnalysisContext context)
    {
        var declaration = invocation.FirstAncestorOrSelf<TypeDeclarationSyntax>();
        var type = declaration is null
            ? null
            : context.SemanticModel.GetDeclaredSymbol(declaration, context.CancellationToken);
        return type is not null
            && (type.Name.EndsWith("Repository", StringComparison.Ordinal)
                || type.AllInterfaces.Any(contract =>
                    contract.Name.EndsWith("Repository", StringComparison.Ordinal)));
    }

    private static bool IsQueryMaterialization(
        IMethodSymbol method,
        ExpressionSyntax source,
        SyntaxNodeAnalysisContext context)
    {
        var containingType = method.ContainingType.ToDisplayString();
        if (method.Name.EndsWith("Async", StringComparison.Ordinal))
        {
            return containingType == "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions";
        }

        return containingType == "System.Linq.Enumerable"
            && OriginatesFromQueryable(source, context);
    }

    private static bool OriginatesFromQueryable(
        ExpressionSyntax source,
        SyntaxNodeAnalysisContext context)
    {
        var sourceType = context.SemanticModel.GetTypeInfo(source, context.CancellationToken).Type;
        if (sourceType is not null
            && (sourceType.OriginalDefinition.ToDisplayString() == "System.Linq.IQueryable<T>"
                || sourceType.AllInterfaces.Any(type =>
                    type.OriginalDefinition.ToDisplayString() == "System.Linq.IQueryable<T>")))
        {
            return true;
        }

        if (source is InvocationExpressionSyntax invocation
            && invocation.Expression is MemberAccessExpressionSyntax member
            && context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol
                is IMethodSymbol method
            && (method.ReducedFrom ?? method).Name == "AsEnumerable")
        {
            return OriginatesFromQueryable(member.Expression, context);
        }

        return false;
    }

    private static bool HasTakeBound(
        ExpressionSyntax expression,
        SyntaxNodeAnalysisContext context,
        HashSet<ILocalSymbol> visited)
    {
        var current = expression;
        while (current is InvocationExpressionSyntax invocation
               && invocation.Expression is MemberAccessExpressionSyntax member)
        {
            var method = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol
                as IMethodSymbol;
            var declared = method?.ReducedFrom ?? method;
            if (declared?.Name == "Take"
                && declared.ContainingType.ToDisplayString()
                    is "System.Linq.Queryable" or "System.Linq.Enumerable")
            {
                return true;
            }
            current = member.Expression;
        }

        if (current is IdentifierNameSyntax identifier
            && context.SemanticModel.GetSymbolInfo(identifier, context.CancellationToken).Symbol
                is ILocalSymbol local
            && visited.Add(local))
        {
            var declarator = local.DeclaringSyntaxReferences
                .Select(reference => reference.GetSyntax(context.CancellationToken))
                .OfType<VariableDeclaratorSyntax>()
                .FirstOrDefault();
            return declarator?.Initializer is not null
                && HasTakeBound(declarator.Initializer.Value, context, visited);
        }

        return false;
    }
}
