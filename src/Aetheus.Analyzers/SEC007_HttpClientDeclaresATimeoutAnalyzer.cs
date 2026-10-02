// SPDX-License-Identifier: EUPL-1.2
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Aetheus.Analyzers;

/// <summary>
/// SEC007: an <c>HttpClient</c> constructed directly states its <c>Timeout</c>.
///
/// The default is 100 seconds. For an agent polling its backend, a health gate, or a probe run by an
/// installer, 100 seconds of silence is a hang, and one that holds a worker, a lease or a deployment
/// window while it lasts. A client that comes from the factory is configured where it is registered,
/// so only the direct construction is checked.
///
/// The timeout counts as declared when it is set in the object initializer, or assigned
/// (<c>x.Timeout = ...</c>) to the variable, field or property the client was stored in, anywhere in
/// the same member (for a local) or the same type (for a field or property).
///
/// Known false positive: a client returned from a factory method whose caller sets the timeout, or
/// one whose requests are bounded instead by a <c>CancellationTokenSource.CancelAfter</c>, is
/// reported: the rule reads one member, not the call graph.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SEC007_HttpClientDeclaresATimeoutAnalyzer : ObjectCreationAnalyzer
{
    public const string DiagnosticId = "SEC007";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "A directly constructed HttpClient sets its Timeout",
        "'{0}' is constructed without a Timeout; the 100-second default turns an unreachable peer into a hang",
        "Aetheus.Security",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The default HttpClient timeout is 100 seconds. Known false positive: the timeout is set by a caller, or requests are bounded by CancelAfter instead.");

    protected override DiagnosticDescriptor Descriptor => Rule;

    protected override void AnalyzeCreation(SyntaxNodeAnalysisContext context)
    {
        var creation = (BaseObjectCreationExpressionSyntax)context.Node;
        var type = context.SemanticModel.GetTypeInfo(creation, context.CancellationToken).Type;
        if (!HttpClientCreation.IsHttpClient(type)) return;
        if (InitializerSetsTimeout(creation) || StoredClientGetsATimeout(creation, context)) return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, creation.GetLocation(), type!.Name));
    }

    private static bool InitializerSetsTimeout(BaseObjectCreationExpressionSyntax creation) =>
        creation.Initializer?.Expressions
            .OfType<AssignmentExpressionSyntax>()
            .Any(assignment => assignment.Left is IdentifierNameSyntax { Identifier.Text: "Timeout" }) == true;

    private static bool StoredClientGetsATimeout(SyntaxNode creation, SyntaxNodeAnalysisContext context)
    {
        var stored = StoredIn(HttpClientCreation.Outermost(creation), context);
        if (stored is null) return false;

        return ScopeOf(stored, creation)?.DescendantNodes().OfType<AssignmentExpressionSyntax>()
            .Any(assignment => AssignsTimeoutOf(assignment, stored, context)) == true;
    }

    /// <summary>
    /// Where an assignment to the stored client can live: the block that declares a local (the whole
    /// file for a top-level statement), the declaring type for a field or property.
    /// </summary>
    private static SyntaxNode? ScopeOf(ISymbol stored, SyntaxNode creation)
    {
        if (stored.Kind != SymbolKind.Local)
            return creation.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
        var declaration = stored.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
        return declaration?.Ancestors().FirstOrDefault(node => node is BlockSyntax or CompilationUnitSyntax);
    }

    /// <summary>The local, field or property the created client is assigned to, if any.</summary>
    private static ISymbol? StoredIn(SyntaxNode value, SyntaxNodeAnalysisContext context)
    {
        return value.Parent switch
        {
            EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax declarator } =>
                context.SemanticModel.GetDeclaredSymbol(declarator, context.CancellationToken),
            EqualsValueClauseSyntax { Parent: PropertyDeclarationSyntax property } =>
                context.SemanticModel.GetDeclaredSymbol(property, context.CancellationToken),
            AssignmentExpressionSyntax assignment when assignment.Right == value =>
                context.SemanticModel.GetSymbolInfo(assignment.Left, context.CancellationToken).Symbol,
            _ => null
        };
    }

    private static bool AssignsTimeoutOf(
        AssignmentExpressionSyntax assignment, ISymbol stored, SyntaxNodeAnalysisContext context)
    {
        if (assignment.Left is not MemberAccessExpressionSyntax { Name.Identifier.Text: "Timeout" } member) return false;
        var target = context.SemanticModel.GetSymbolInfo(member.Expression, context.CancellationToken).Symbol;
        return SymbolEqualityComparer.Default.Equals(target, stored);
    }
}
