// SPDX-License-Identifier: EUPL-1.2
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Aetheus.Analyzers;

/// <summary>
/// SEC002: a controller must say whether it is authenticated, in its own source.
///
/// A missing <c>[Authorize]</c> is not a wrong line, it is an absent one, so nothing about the file
/// looks unusual: the endpoint reads exactly like its neighbours and is simply open. Aetheus exposes
/// deployment, artifact and secret-adjacent controllers, so the cost of one such omission is not
/// proportional to how easy it is to make.
///
/// The rule accepts either answer, never one of them: <c>[Authorize]</c> or <c>[AllowAnonymous]</c>.
/// Anonymous access is a legitimate decision (health, webhooks, the public ingest endpoint) and this
/// asks only that the decision be written down where the next reader will see it.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SEC002_EndpointDeclaresItsAuthorizationAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "SEC002";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "A controller must declare its authorization",
        "Controller '{0}' declares neither [Authorize] nor [AllowAnonymous]; state which one it is",
        "Aetheus.Security",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "An endpoint without an authorization attribute is open, and looks exactly like one that is not. Requiring an explicit attribute turns a silent omission into a visible decision.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeClass, SyntaxKind.ClassDeclaration);
    }

    private static void AnalyzeClass(SyntaxNodeAnalysisContext context)
    {
        var declaration = (ClassDeclarationSyntax)context.Node;
        if (context.SemanticModel.GetDeclaredSymbol(declaration, context.CancellationToken)
            is not INamedTypeSymbol type)
        {
            return;
        }
        // An abstract base carries shared plumbing, not routes of its own; the concrete controller
        // that derives from it is what must declare the decision.
        if (type.IsAbstract || !ControllerAuthorization.IsController(type)) return;
        if (DeclaresAuthorization(type)) return;

        // Declaring it per action is equally explicit, and sometimes the only way to say it: the Git
        // smart-HTTP endpoints each name their own authentication scheme. The class is only reported
        // when at least one action still says nothing, which is the case this rule exists for.
        var undeclared = declaration.Members
            .OfType<MethodDeclarationSyntax>()
            .Select(method => context.SemanticModel.GetDeclaredSymbol(method, context.CancellationToken))
            .OfType<IMethodSymbol>()
            .Where(IsAction)
            .Where(method => !method.GetAttributes().Any(ControllerAuthorization.IsAuthorizationAttribute))
            .ToList();
        if (undeclared.Count == 0 && declaration.Members.OfType<MethodDeclarationSyntax>().Any()) return;

        context.ReportDiagnostic(Diagnostic.Create(
            Rule, declaration.Identifier.GetLocation(), type.Name));
    }

    /// <summary>A publicly reachable action, which is what a route can hit.</summary>
    private static bool IsAction(IMethodSymbol method) =>
        method is { DeclaredAccessibility: Accessibility.Public, MethodKind: MethodKind.Ordinary, IsStatic: false };

    /// <summary>
    /// Walks the inheritance chain: a controller inheriting from an authorized base is authorized,
    /// and demanding a repeated attribute would push people to copy one they do not read.
    /// </summary>
    private static bool DeclaresAuthorization(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
            if (current.GetAttributes().Any(ControllerAuthorization.IsAuthorizationAttribute))
                return true;
        return false;
    }
}
