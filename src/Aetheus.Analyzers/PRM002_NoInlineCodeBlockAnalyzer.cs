// SPDX-License-Identifier: EUPL-1.2
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Aetheus.Analyzers;

/// <summary>
/// PRM002: Forbids @code blocks in .razor files. Use code-behind (.razor.cs) instead.
/// Razor inputs are supplied centrally by Directory.Build.targets.
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

        var source = text.ToString();
        var masked = MaskComments(source);
        foreach (var line in text.Lines)
        {
            var lineText = masked.Substring(line.Start, line.End - line.Start);
            var leadingWhitespace = lineText.Length - lineText.TrimStart().Length;
            var directive = StartsDirective(masked, line.Start + leadingWhitespace, "@code")
                || StartsDirective(masked, line.Start + leadingWhitespace, "@functions");
            if (directive)
            {
                var diagnostic = Diagnostic.Create(Rule,
                    Location.Create(context.AdditionalFile.Path,
                        TextSpan.FromBounds(line.Start, line.End),
                        new LinePositionSpan(
                            new LinePosition(line.LineNumber, 0),
                            new LinePosition(line.LineNumber, line.End - line.Start))));
                context.ReportDiagnostic(diagnostic);
            }
        }
    }

    private static bool StartsDirective(string source, int start, string directive)
    {
        if (start + directive.Length > source.Length
            || !source.AsSpan(start, directive.Length).SequenceEqual(directive.AsSpan()))
        {
            return false;
        }

        var cursor = start + directive.Length;
        if (cursor < source.Length && !char.IsWhiteSpace(source[cursor]) && source[cursor] != '{')
            return false;
        while (cursor < source.Length && char.IsWhiteSpace(source[cursor]))
            cursor++;
        return cursor < source.Length && source[cursor] == '{';
    }

    private static string MaskComments(string source)
    {
        var chars = source.ToCharArray();
        MaskDelimited(chars, source, "@*", "*@");
        MaskDelimited(chars, source, "<!--", "-->");
        return new string(chars);
    }

    private static void MaskDelimited(char[] chars, string source, string opening, string closing)
    {
        var cursor = 0;
        while ((cursor = source.IndexOf(opening, cursor, System.StringComparison.Ordinal)) >= 0)
        {
            var end = source.IndexOf(closing, cursor + opening.Length, System.StringComparison.Ordinal);
            end = end < 0 ? source.Length : end + closing.Length;
            for (var index = cursor; index < end; index++)
            {
                if (chars[index] is not ('\r' or '\n'))
                    chars[index] = ' ';
            }
            cursor = end;
        }
    }
}
