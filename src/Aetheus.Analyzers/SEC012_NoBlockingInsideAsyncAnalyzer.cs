// SPDX-License-Identifier: EUPL-1.2
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Aetheus.Analyzers;

/// <summary>
/// SEC012: an <c>async</c> function does not block its thread.
///
/// Reported inside an <c>async</c> method, local function or lambda: <c>.Result</c> on a
/// <c>Task&lt;T&gt;</c> or <c>ValueTask&lt;T&gt;</c>, <c>.Wait()</c> on a <c>Task</c>,
/// <c>.GetAwaiter().GetResult()</c>, and <c>Thread.Sleep</c>. Each one holds a thread-pool thread
/// for as long as the work takes; under load the pool starves and every request stalls, and where a
/// synchronization context exists (Blazor) it deadlocks outright. It is classed as security because
/// that starvation is a denial of service any slow dependency can trigger.
///
/// Only the innermost function counts: a synchronous lambda inside an async method is its own
/// function and is not reported. A task already known to be complete is not reported either,
/// because nothing blocks: one awaited earlier in the same function (<c>await t</c>,
/// <c>await Task.WhenAll(a, b)</c>), or one read in the true branch of a conditional or an <c>if</c>
/// that tests its completion (<c>t.Status == TaskStatus.RanToCompletion ? t.Result : fallback</c>).
///
/// Known false positive: a task proven complete by an earlier early return
/// (<c>if (!t.IsCompleted) return; ... t.Result</c>) or by a helper is reported. Known false
/// negative: the guard is recognised by name, so a negated test (<c>!t.IsCompleted ? t.Result : x</c>)
/// is treated as a guard.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SEC012_NoBlockingInsideAsyncAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "SEC012";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Do not block inside an async function",
        "'{0}' blocks a thread inside an async function; await it instead",
        "Aetheus.Security",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Blocking on a task or sleeping inside async code starves the thread pool and deadlocks under a synchronization context. Known false positive: .Result on a task proven complete by an earlier early return or by a helper.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeMemberAccess, SyntaxKind.SimpleMemberAccessExpression);
    }

    private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
    {
        var member = (MemberAccessExpressionSyntax)context.Node;
        if (member.Name.Identifier.Text is not ("Result" or "Wait" or "GetResult" or "Sleep")) return;
        var function = InnermostFunction(member);
        if (function is null || !IsAsync(function)) return;

        var symbol = context.SemanticModel.GetSymbolInfo(member, context.CancellationToken).Symbol;
        var blocking = Describe(symbol);
        if (blocking is null) return;
        if (blocking != "Thread.Sleep" && IsKnownComplete(member, function, context)) return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, member.Name.GetLocation(), blocking));
    }

    private static bool IsKnownComplete(
        MemberAccessExpressionSyntax member, SyntaxNode function, SyntaxNodeAnalysisContext context)
    {
        var root = context.SemanticModel.GetSymbolInfo(TaskOf(member), context.CancellationToken).Symbol;
        if (root is not (ILocalSymbol or IParameterSymbol or IFieldSymbol or IPropertySymbol)) return false;
        return WasAwaitedBefore(root, function, member.SpanStart, context)
            || IsGuardedByCompletion(member, root, function, context);
    }

    /// <summary>Names the blocking call, or null when the member is not one.</summary>
    private static string? Describe(ISymbol? symbol)
    {
        var type = symbol?.ContainingType?.OriginalDefinition.ToDisplayString() ?? string.Empty;
        return symbol switch
        {
            IPropertySymbol { Name: "Result" } when type is "System.Threading.Tasks.Task<TResult>"
                or "System.Threading.Tasks.ValueTask<TResult>" => ".Result",
            IMethodSymbol { Name: "Wait" } when type == "System.Threading.Tasks.Task" => ".Wait()",
            IMethodSymbol { Name: "GetResult" } when type.StartsWith("System.Runtime.CompilerServices.", System.StringComparison.Ordinal)
                && type.Contains("Awaiter") => ".GetAwaiter().GetResult()",
            IMethodSymbol { Name: "Sleep" } when type == "System.Threading.Thread" => "Thread.Sleep",
            _ => null
        };
    }

    private static SyntaxNode? InnermostFunction(SyntaxNode node) =>
        node.Ancestors().FirstOrDefault(ancestor => ancestor is AnonymousFunctionExpressionSyntax
            or LocalFunctionStatementSyntax or BaseMethodDeclarationSyntax or AccessorDeclarationSyntax
            or PropertyDeclarationSyntax or IndexerDeclarationSyntax);

    private static bool IsAsync(SyntaxNode function) => function switch
    {
        AnonymousFunctionExpressionSyntax lambda => lambda.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword),
        LocalFunctionStatementSyntax local => local.Modifiers.Any(SyntaxKind.AsyncKeyword),
        MethodDeclarationSyntax method => method.Modifiers.Any(SyntaxKind.AsyncKeyword),
        _ => false
    };

    /// <summary>The task expression: `t` in `t.Result`, `t` in `t.GetAwaiter().GetResult()`.</summary>
    private static ExpressionSyntax TaskOf(MemberAccessExpressionSyntax member)
    {
        var receiver = member.Expression;
        if (receiver is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.Text: "GetAwaiter" or "ConfigureAwait" } inner })
            receiver = inner.Expression;
        if (receiver is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.Text: "ConfigureAwait" } configured })
            receiver = configured.Expression;
        while (receiver is ElementAccessExpressionSyntax element) receiver = element.Expression;
        return receiver;
    }

    /// <summary>
    /// True when the task's root variable appears inside an `await` that precedes the blocking call in
    /// the same function: `await t`, `await Task.WhenAll(a, b)`, `await Task.WhenAll(tasks)`.
    /// </summary>
    private static bool WasAwaitedBefore(
        ISymbol root, SyntaxNode function, int position, SyntaxNodeAnalysisContext context) =>
        function.DescendantNodes()
            .OfType<AwaitExpressionSyntax>()
            .Where(awaited => awaited.SpanStart < position)
            .Any(awaited => Mentions(awaited.Expression, root, context));

    /// <summary>
    /// True when the blocking call sits in the true branch of a conditional or an <c>if</c> whose
    /// condition tests the same task's completion: <c>t.Status == TaskStatus.RanToCompletion ? t.Result : x</c>,
    /// <c>if (t.IsCompletedSuccessfully) return t.Result;</c>.
    /// </summary>
    private static bool IsGuardedByCompletion(
        SyntaxNode node, ISymbol root, SyntaxNode function, SyntaxNodeAnalysisContext context)
    {
        for (var child = node; child != function && child.Parent is { } parent; child = parent)
        {
            var condition = parent switch
            {
                ConditionalExpressionSyntax conditional when conditional.WhenTrue == child => conditional.Condition,
                IfStatementSyntax statement when statement.Statement == child => statement.Condition,
                _ => null
            };
            if (condition is not null && TestsCompletion(condition) && Mentions(condition, root, context)) return true;
        }
        return false;
    }

    private static bool TestsCompletion(ExpressionSyntax condition) =>
        condition.DescendantNodesAndSelf().OfType<SimpleNameSyntax>().Any(name =>
            name.Identifier.Text is "IsCompleted" or "IsCompletedSuccessfully" or "RanToCompletion");

    private static bool Mentions(SyntaxNode node, ISymbol root, SyntaxNodeAnalysisContext context) =>
        node.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>().Any(identifier =>
            SymbolEqualityComparer.Default.Equals(
                context.SemanticModel.GetSymbolInfo(identifier, context.CancellationToken).Symbol, root));
}
