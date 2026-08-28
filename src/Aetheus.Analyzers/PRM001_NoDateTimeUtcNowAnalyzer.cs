// SPDX-License-Identifier: EUPL-1.2
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Aetheus.Analyzers;

/// <summary>
/// PRM001: Forbids ambient DateTime and DateTimeOffset clocks in production code.
/// Use injected TimeProvider instead.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PRM001_NoDateTimeUtcNowAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "PRM001";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Avoid ambient DateTime / DateTimeOffset clocks",
        "Use injected TimeProvider instead of {0}",
        "Aetheus.TimeAbstraction",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Ambient DateTime and DateTimeOffset clocks bypass the TimeProvider abstraction, making code untestable and inconsistent.");

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

        if (memberName is not ("UtcNow" or "Now" or "Today")) return;

        // Resolve the containing symbol instead of matching source text so aliases and
        // global::System.DateTime cannot bypass the rule.
        if (context.SemanticModel.GetSymbolInfo(memberAccess, context.CancellationToken).Symbol
            is not IPropertySymbol property
            || !IsAmbientClockProperty(property))
        {
            return;
        }

        var filePath = memberAccess.SyntaxTree.FilePath;
        if (IsEfMigration(memberAccess, context, filePath)) return;

        var diagnostic = Diagnostic.Create(
            Rule,
            memberAccess.GetLocation(),
            $"{property.ContainingType.Name}.{memberName}");
        context.ReportDiagnostic(diagnostic);
    }

    private static bool IsAmbientClockProperty(IPropertySymbol property) =>
        property.ContainingType.SpecialType == SpecialType.System_DateTime
            ? property.Name is "UtcNow" or "Now" or "Today"
            : property.ContainingType.ToDisplayString() == "System.DateTimeOffset"
                && property.Name is "UtcNow" or "Now";

    private static bool IsEfMigration(
        MemberAccessExpressionSyntax memberAccess,
        SyntaxNodeAnalysisContext context,
        string filePath)
    {
        var dir = System.IO.Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(dir)) return false;
        var normalized = dir.Replace('\\', '/');
        if (!normalized.EndsWith("/Migrations", System.StringComparison.OrdinalIgnoreCase)
            && !normalized.Contains("/Migrations/", System.StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var declaration = memberAccess.FirstAncestorOrSelf<ClassDeclarationSyntax>();
        var type = declaration is null
            ? null
            : context.SemanticModel.GetDeclaredSymbol(declaration, context.CancellationToken);
        while (type is not null)
        {
            if (type.ToDisplayString() == "Microsoft.EntityFrameworkCore.Migrations.Migration")
                return true;
            type = type.BaseType;
        }
        return false;
    }
}
