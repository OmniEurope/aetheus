// SPDX-License-Identifier: EUPL-1.2
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Aetheus.Analyzers.Tests;

/// <summary>
/// Thin wrapper over the Roslyn analyzer testing harness that runs a single source file
/// at a caller-controlled path (so the analyzers' path filters are exercised) against a
/// fixed set of expected diagnostics. The analyzers are purely syntactic, so Net80
/// reference assemblies and minimal parseable snippets are sufficient.
/// </summary>
internal static class Verifier<TAnalyzer>
    where TAnalyzer : DiagnosticAnalyzer, new()
{
    /// <summary>
    /// Builds an expected diagnostic anchored to <paramref name="filePath"/> - the same path the
    /// source is added under - so the span comparison matches the custom file name rather than the
    /// harness's synthetic "/0/Test0.cs".
    /// </summary>
    public static DiagnosticResult Expect(
        string diagnosticId, DiagnosticSeverity severity,
        string filePath, int startLine, int startColumn, int endLine, int endColumn,
        params object[] arguments)
        => new DiagnosticResult(diagnosticId, severity)
            .WithSpan(filePath, startLine, startColumn, endLine, endColumn)
            .WithArguments(arguments);

    public static async Task VerifyAsync(string filePath, string source, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };
        test.TestState.Sources.Add((filePath, source));
        test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync();
    }

    /// <summary>
    /// Variant for analyzers that run via <c>RegisterAdditionalFileAction</c> (e.g. PRM002 on
    /// <c>.razor</c> files). The content is added as an additional file at <paramref name="filePath"/>;
    /// a trivial compile source is supplied because the harness requires at least one.
    /// </summary>
    public static async Task VerifyAdditionalFileAsync(string filePath, string content, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };
        test.TestState.Sources.Add(("Dummy.cs", "// no code"));
        test.TestState.AdditionalFiles.Add((filePath, content));
        test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync();
    }
}
