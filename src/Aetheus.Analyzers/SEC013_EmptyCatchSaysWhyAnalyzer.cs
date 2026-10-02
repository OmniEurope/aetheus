// SPDX-License-Identifier: EUPL-1.2
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Aetheus.Analyzers;

/// <summary>
/// SEC013: a <c>catch</c> that swallows an exception says why.
///
/// An empty catch is where a failed security check, a refused write or a lost audit event goes to
/// disappear. The codebase's convention is already that an intentional swallow carries its reason
/// (<c>// A logging sink must never throw into the app.</c>), so the rule enforces the convention
/// rather than forbidding the construct: a catch with no statement is accepted when it has a comment
/// anywhere in the clause, including on the line of its closing brace
/// (<c>catch (HttpRequestException) { } // 401 handled by AuthProvider</c>), or directly above its
/// <c>try</c> (the one-line <c>try { X(); } catch (E) { }</c> idiom).
///
/// Also accepted without a comment, because the code itself states the reason: catching
/// <c>OperationCanceledException</c> (or <c>TaskCanceledException</c>), the normal way a cancelled
/// operation ends, and any catch whose <c>when</c> filter tests <c>IsCancellationRequested</c>.
///
/// Known false positive: a swallow whose reason is written somewhere else (the method's XML summary,
/// the comment of an enclosing block) is reported, because the reason has to sit at the catch to
/// survive the next edit of that block. Known false negative: any comment is accepted as a reason,
/// including one that says nothing.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SEC013_EmptyCatchSaysWhyAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "SEC013";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "An empty catch says why the exception is swallowed",
        "This catch swallows {0} without saying why; handle it, or write the reason as a comment in the block",
        "Aetheus.Security",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "An unexplained empty catch hides failures, including security ones. Known false positive: the reason is written elsewhere than the catch clause or its try (a method summary, an enclosing block's comment).");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeCatch, SyntaxKind.CatchClause);
    }

    private static void AnalyzeCatch(SyntaxNodeAnalysisContext context)
    {
        var clause = (CatchClauseSyntax)context.Node;
        if (clause.Block.Statements.Count > 0 || HasComment(clause) || FilterTestsCancellation(clause)) return;

        var caught = clause.Declaration is null
            ? null
            : context.SemanticModel.GetTypeInfo(clause.Declaration.Type, context.CancellationToken).Type;
        if (IsCancellation(caught)) return;

        var what = caught is null ? "every exception" : $"'{caught.Name}'";
        context.ReportDiagnostic(Diagnostic.Create(Rule, clause.CatchKeyword.GetLocation(), what));
    }

    /// <summary>
    /// A comment in the clause itself, or directly above its <c>try</c>: in the one-line
    /// <c>try { X(); } catch (E) { }</c> idiom that is where the reason is written.
    /// </summary>
    private static bool HasComment(CatchClauseSyntax clause) =>
        clause.DescendantTrivia(descendIntoTrivia: true).Any(IsComment)
        || clause.Parent is TryStatementSyntax statement && statement.GetLeadingTrivia().Any(IsComment);

    private static bool IsComment(SyntaxTrivia trivia) =>
        trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia);

    private static bool FilterTestsCancellation(CatchClauseSyntax clause) =>
        clause.Filter?.FilterExpression.DescendantNodesAndSelf().OfType<SimpleNameSyntax>()
            .Any(name => name.Identifier.Text == "IsCancellationRequested") == true;

    private static bool IsCancellation(ITypeSymbol? type)
    {
        for (var current = type; current is not null; current = current.BaseType)
            if (current.ToDisplayString() == "System.OperationCanceledException")
                return true;
        return false;
    }
}
