// SPDX-License-Identifier: EUPL-1.2
using Microsoft.CodeAnalysis;

namespace Aetheus.Analyzers.Tests;

public sealed class Prm002Tests
{
    [Fact]
    public async Task CodeBlock_InRazor_ReportsDiagnostic()
    {
        // Line 2 holds "@code {". The analyzer spans the whole line (col 1 to length+1 = 8).
        const string razor = "<div>Hello</div>\n@code {\n    private int X = 1;\n}\n";

        var expected = Verifier<PRM002_NoInlineCodeBlockAnalyzer>.Expect(
            "PRM002", DiagnosticSeverity.Warning, "Foo.razor", 2, 1, 2, 8);

        await Verifier<PRM002_NoInlineCodeBlockAnalyzer>.VerifyAdditionalFileAsync("Foo.razor", razor, expected);
    }

    [Fact]
    public async Task NoCodeBlock_InRazor_NoDiagnostic()
    {
        const string razor = "<div>Hello</div>\n<p>No code-behind violation here.</p>\n";

        await Verifier<PRM002_NoInlineCodeBlockAnalyzer>.VerifyAdditionalFileAsync("Foo.razor", razor);
    }

    [Fact]
    public async Task CodeBlock_InNonRazorFile_NoDiagnostic()
    {
        // The path filter only fires on .razor; a .txt additional file with @code is ignored.
        const string content = "@code {\n}\n";

        await Verifier<PRM002_NoInlineCodeBlockAnalyzer>.VerifyAdditionalFileAsync("Notes.txt", content);
    }

    [Fact]
    public async Task FunctionsBlock_InRazor_ReportsDiagnostic()
    {
        const string razor = "<div>Hello</div>\n@functions {\n    private int X = 1;\n}\n";
        var expected = Verifier<PRM002_NoInlineCodeBlockAnalyzer>.Expect(
            "PRM002", DiagnosticSeverity.Warning, "Functions.razor", 2, 1, 2, 13);

        await Verifier<PRM002_NoInlineCodeBlockAnalyzer>.VerifyAdditionalFileAsync(
            "Functions.razor", razor, expected);
    }

    [Fact]
    public async Task SimilarDirectiveName_DoesNotReportDiagnostic()
    {
        const string razor = "@codeValue { }\n@functionsHelper { }\n";
        await Verifier<PRM002_NoInlineCodeBlockAnalyzer>.VerifyAdditionalFileAsync("Names.razor", razor);
    }

    [Fact]
    public async Task RazorAndHtmlComments_DoNotReportDiagnostic()
    {
        const string razor = "@*\n@code { }\n*@\n<!--\n@functions { }\n-->\n";

        await Verifier<PRM002_NoInlineCodeBlockAnalyzer>.VerifyAdditionalFileAsync(
            "Comments.razor", razor);
    }

    [Fact]
    public async Task EveryInlineBlock_ReportsItsOwnDiagnostic()
    {
        const string razor = "@code { }\n<div />\n@functions\n{\n}\n";
        var first = Verifier<PRM002_NoInlineCodeBlockAnalyzer>.Expect(
            "PRM002", DiagnosticSeverity.Warning, "Multiple.razor", 1, 1, 1, 10);
        var second = Verifier<PRM002_NoInlineCodeBlockAnalyzer>.Expect(
            "PRM002", DiagnosticSeverity.Warning, "Multiple.razor", 3, 1, 3, 11);

        await Verifier<PRM002_NoInlineCodeBlockAnalyzer>.VerifyAdditionalFileAsync(
            "Multiple.razor", razor, first, second);
    }
}
