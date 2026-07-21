// SPDX-License-Identifier: EUPL-1.2
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Aetheus.Agent.Core.Operations;

/// <summary>L: aggregate code-quality metrics for a set of C# source files.</summary>
public sealed record ComplexityReport(
    int TotalMethods, int TotalLinesOfCode, double AvgCyclomatic, int MaxCyclomatic, int HighComplexityMethods);

/// <summary>
/// L: real cyclomatic-complexity analysis over C# sources via Roslyn syntax trees (no semantic model,
/// so it runs on raw files without a build). Cyclomatic complexity per method = 1 + the number of
/// decision points (branches, loops, switch arms, catches, short-circuit operators). A method's
/// nested local functions roll into its complexity; nested types/methods are counted on their own.
/// </summary>
public static class ComplexityAnalyzer
{
    public const int HighComplexityThreshold = 10;

    public static ComplexityReport Analyze(IEnumerable<(string Path, string Content)> files)
    {
        var complexities = new List<int>();
        var totalNloc = 0;

        foreach (var (_, content) in files)
        {
            if (string.IsNullOrWhiteSpace(content)) continue;
            totalNloc += CountNonCommentLines(content);

            var root = CSharpSyntaxTree.ParseText(content).GetRoot();
            foreach (var node in root.DescendantNodes())
            {
                var body = MemberBody(node);
                if (body is not null)
                    complexities.Add(CyclomaticComplexity(body));
            }
        }

        if (complexities.Count == 0)
            return new ComplexityReport(0, totalNloc, 0, 0, 0);

        return new ComplexityReport(
            TotalMethods: complexities.Count,
            TotalLinesOfCode: totalNloc,
            AvgCyclomatic: Math.Round(complexities.Average(), 2),
            MaxCyclomatic: complexities.Max(),
            HighComplexityMethods: complexities.Count(c => c > HighComplexityThreshold));
    }

    // Bodies that count as a "method" unit. Local functions are intentionally excluded here so their
    // complexity rolls into the enclosing member rather than being double-counted.
    private static SyntaxNode? MemberBody(SyntaxNode node) => node switch
    {
        BaseMethodDeclarationSyntax m => (SyntaxNode?)m.Body ?? m.ExpressionBody,
        AccessorDeclarationSyntax a => (SyntaxNode?)a.Body ?? a.ExpressionBody,
        _ => null
    };

    private static int CyclomaticComplexity(SyntaxNode body)
    {
        var complexity = 1;
        foreach (var node in body.DescendantNodesAndSelf())
        {
            switch (node)
            {
                case IfStatementSyntax:
                case WhileStatementSyntax:
                case DoStatementSyntax:
                case ForStatementSyntax:
                case ForEachStatementSyntax:
                case CaseSwitchLabelSyntax:
                case CasePatternSwitchLabelSyntax:
                case SwitchExpressionArmSyntax:
                case CatchClauseSyntax:
                case ConditionalExpressionSyntax:
                    complexity++;
                    break;
                case BinaryExpressionSyntax be when be.IsKind(SyntaxKind.LogicalAndExpression)
                                                 || be.IsKind(SyntaxKind.LogicalOrExpression)
                                                 || be.IsKind(SyntaxKind.CoalesceExpression):
                    complexity++;
                    break;
            }
        }
        return complexity;
    }

    private static int CountNonCommentLines(string content)
    {
        var count = 0;
        var inBlockComment = false;
        foreach (var raw in content.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (inBlockComment)
            {
                if (line.Contains("*/")) inBlockComment = false;
                continue;
            }
            if (line.StartsWith("//", StringComparison.Ordinal)) continue;
            if (line.StartsWith("/*", StringComparison.Ordinal))
            {
                if (!line.Contains("*/")) inBlockComment = true;
                continue;
            }
            count++;
        }
        return count;
    }
}
