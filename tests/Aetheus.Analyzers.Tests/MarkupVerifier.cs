// SPDX-License-Identifier: EUPL-1.2
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Aetheus.Analyzers.Tests;

/// <summary>
/// Runs an analyzer over sources whose expected diagnostics are written in place with the harness
/// markup <c>{|SEC006:span|}</c>. Every diagnostic produced must be marked and every mark must be
/// produced, so a test that marks nothing is a proof of silence, not an absence of assertion.
/// Used by the SEC006+ rules, whose snippets are long enough that line/column pairs would be the
/// part of the test nobody could review.
/// </summary>
internal static class MarkupVerifier<TAnalyzer>
    where TAnalyzer : DiagnosticAnalyzer, new()
{
    public static async Task VerifyAsync(params string[] sources)
    {
        var test = new CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };
        for (var i = 0; i < sources.Length; i++)
            test.TestState.Sources.Add(($"Source{i}.cs", sources[i]));
        await test.RunAsync();
    }
}
