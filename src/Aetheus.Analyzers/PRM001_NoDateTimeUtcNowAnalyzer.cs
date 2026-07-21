// SPDX-License-Identifier: EUPL-1.2
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Aetheus.Analyzers;

/// <summary>
/// PRM001: Forbids DateTime.UtcNow and DateTime.Now in production backend code.
/// Use injected TimeProvider instead.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PRM001_NoDateTimeUtcNowAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "PRM001";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Avoid DateTime.UtcNow / DateTime.Now",
        "Use injected TimeProvider instead of {0}",
        "Aetheus.TimeAbstraction",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "DateTime.UtcNow and DateTime.Now bypass the TimeProvider abstraction, making code untestable and inconsistent.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeMemberAccess, SyntaxKind.SimpleMemberAccessExpression);
    }

    private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
    {
        var memberAccess = (MemberAccessExpressionSyntax)context.Node;
        var memberName = memberAccess.Name.Identifier.Text;

        if (memberName is not ("UtcNow" or "Now")) return;

        // Resolve the containing symbol instead of matching source text so aliases and
        // global::System.DateTime cannot bypass the rule.
        if (context.SemanticModel.GetSymbolInfo(memberAccess, context.CancellationToken).Symbol
            is not IPropertySymbol property
            || property.ContainingType.SpecialType != SpecialType.System_DateTime)
        {
            return;
        }

        // Skip if in a Migrations folder
        var filePath = memberAccess.SyntaxTree.FilePath;
        if (IsInMigrationsFolder(filePath)) return;

        var diagnostic = Diagnostic.Create(Rule, memberAccess.GetLocation(), $"DateTime.{memberName}");
        context.ReportDiagnostic(diagnostic);
    }

    private static bool IsInMigrationsFolder(string filePath)
    {
        var dir = System.IO.Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(dir)) return false;
        var normalized = dir.Replace('\\', '/');
        return normalized.EndsWith("/Migrations", System.StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/Migrations/", System.StringComparison.OrdinalIgnoreCase);
    }
}
