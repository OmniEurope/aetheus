// SPDX-License-Identifier: EUPL-1.2
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Aetheus.Analyzers;

/// <summary>
/// PRM002: Forbids @code blocks in .razor files. Use code-behind (.razor.cs) instead.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PRM002_NoInlineCodeBlockAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "PRM002";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "No inline @code in .razor files",
        "Move @code block to a code-behind .razor.cs file",
        "Aetheus.BlazorConventions",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Inline @code blocks in .razor files violate the code-behind convention.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterAdditionalFileAction(AnalyzeAdditionalFile);
    }

    private static void AnalyzeAdditionalFile(AdditionalFileAnalysisContext context)
    {
        if (!context.AdditionalFile.Path.EndsWith(".razor", System.StringComparison.OrdinalIgnoreCase))
            return;

        var text = context.AdditionalFile.GetText(context.CancellationToken);
        if (text is null) return;

        foreach (var line in text.Lines)
        {
            var lineText = line.ToString();
            var trimmed = lineText.TrimStart();
            if (StartsDirective(trimmed, "@code") || StartsDirective(trimmed, "@functions"))
            {
                var diagnostic = Diagnostic.Create(Rule,
                    Location.Create(context.AdditionalFile.Path,
                        TextSpan.FromBounds(line.Start, line.End),
                        new LinePositionSpan(
                            new LinePosition(line.LineNumber, 0),
                            new LinePosition(line.LineNumber, lineText.Length))));
                context.ReportDiagnostic(diagnostic);
                break; // One per file is enough
            }
        }
    }

    private static bool StartsDirective(string line, string directive) =>
        line.StartsWith(directive, System.StringComparison.Ordinal)
        && (line.Length == directive.Length || char.IsWhiteSpace(line[directive.Length]) || line[directive.Length] == '{');
}
