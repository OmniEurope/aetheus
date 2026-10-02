// SPDX-License-Identifier: EUPL-1.2
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Aetheus.Analyzers;

/// <summary>
/// SEC005: a value that came in with the request must not reach raw SQL, a process, or a file path,
/// inside the method that received it.
///
/// Aetheus takes a lot of caller-controlled text and hands it to things that act on the host: a
/// pipeline definition, an artifact name, a repository slug, a workspace path. The rule follows the
/// short, certain case - a parameter of an action method used directly in a sink in that same method
/// - because that is the shape a reviewer misses and a taint engine costs a build for.
///
/// It deliberately does NOT try to be a dataflow analyser. It misses a value passed through a
/// helper, and says so rather than pretending otherwise: a rule that fires on maybes gets switched
/// off, and one that fires on the certain case gets fixed.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SEC005_NoRequestDataInDangerousSinkAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "SEC005";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Request data must not reach raw SQL, a process or a file path unchecked",
        "'{0}' comes from the request and reaches {1}; validate it against an allow-list or use a parameterised API",
        "Aetheus.Security",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A caller-controlled string handed straight to raw SQL, a process or a path resolves to whatever the caller wanted it to resolve to.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    /// <summary>Attributes that say "this value came in with the request".</summary>
    private static readonly string[] BindingAttributes =
    [
        "Microsoft.AspNetCore.Mvc.FromQueryAttribute",
        "Microsoft.AspNetCore.Mvc.FromRouteAttribute",
        "Microsoft.AspNetCore.Mvc.FromBodyAttribute",
        "Microsoft.AspNetCore.Mvc.FromFormAttribute",
        "Microsoft.AspNetCore.Mvc.FromHeaderAttribute"
    ];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
    }

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext context)
    {
        var method = (MethodDeclarationSyntax)context.Node;
        if (context.SemanticModel.GetDeclaredSymbol(method, context.CancellationToken)
            is not IMethodSymbol symbol)
        {
            return;
        }

        var tainted = symbol.Parameters
            .Where(IsRequestBound)
            .Select(parameter => parameter.Name)
            .ToImmutableHashSet(System.StringComparer.Ordinal);
        if (tainted.IsEmpty) return;

        foreach (var invocation in method.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var sink = DescribeSink(invocation, context);
            if (sink is null) continue;
            foreach (var argument in invocation.ArgumentList.Arguments)
                ReportTainted(argument.Expression, tainted, sink, context);
        }
    }

    /// <summary>
    /// A parameter is request-bound when it says so, or when the method is an action and the
    /// parameter is a plain string: ASP.NET binds those from the route or the query with no
    /// attribute at all, which is exactly the case a reader forgets.
    /// </summary>
    private static bool IsRequestBound(IParameterSymbol parameter)
    {
        if (parameter.GetAttributes().Any(IsBindingAttribute)) return true;
        return parameter.Type.SpecialType == SpecialType.System_String
            && parameter.ContainingSymbol is IMethodSymbol { DeclaredAccessibility: Accessibility.Public } method
            && IsController(method.ContainingType);
    }

    private static bool IsBindingAttribute(AttributeData attribute)
    {
        var name = attribute.AttributeClass?.ToDisplayString() ?? string.Empty;
        return System.Array.IndexOf(BindingAttributes, name) >= 0;
    }

    private static bool IsController(INamedTypeSymbol? type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var name = current.ToDisplayString();
            if (name is "Microsoft.AspNetCore.Mvc.ControllerBase" or "Microsoft.AspNetCore.Mvc.Controller")
                return true;
        }
        return false;
    }

    /// <summary>Names the sink when this call acts on the host with its arguments.</summary>
    private static string? DescribeSink(InvocationExpressionSyntax invocation, SyntaxNodeAnalysisContext context)
    {
        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol
            is not IMethodSymbol method)
        {
            return null;
        }
        var containing = method.ContainingType?.ToDisplayString() ?? string.Empty;
        var name = method.Name;

        // Raw SQL. The interpolated overloads (FromSql, ExecuteSql) are parameterised by the
        // compiler and are the correct answer, so only the Raw ones are sinks.
        if (name is "FromSqlRaw" or "ExecuteSqlRaw" or "ExecuteSqlRawAsync") return "raw SQL";
        if (containing == "System.Diagnostics.Process" && name is "Start") return "a process";
        if (containing == "System.IO.Path" && name is "Combine" or "Join") return "a file path";
        if (containing is "System.IO.File" or "System.IO.Directory") return "the file system";
        return null;
    }

    private static void ReportTainted(
        ExpressionSyntax expression,
        ImmutableHashSet<string> tainted,
        string sink,
        SyntaxNodeAnalysisContext context)
    {
        foreach (var identifier in expression.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
        {
            if (!tainted.Contains(identifier.Identifier.Text)) continue;
            // The parameter itself, not a member of something that happens to share its name.
            if (context.SemanticModel.GetSymbolInfo(identifier, context.CancellationToken).Symbol
                is not IParameterSymbol)
            {
                continue;
            }
            context.ReportDiagnostic(Diagnostic.Create(
                Rule, identifier.GetLocation(), identifier.Identifier.Text, sink));
        }
    }
}
