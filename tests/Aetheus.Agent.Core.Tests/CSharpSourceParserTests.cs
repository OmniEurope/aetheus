// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Agent.Core.Operations;

namespace Aetheus.Agent.Core.Tests;

public sealed class CSharpSourceParserTests
{
    [Fact]
    public void Parse_IsDeterministicAcrossSequentialAndParallelBudgets()
    {
        var sources = Enumerable.Range(0, 20)
            .Select(index => ($"File{index}.cs", $"namespace N; class C{index} {{ void M() {{ if (true) {{ }} }} }}"))
            .ToList();

        var sequential = CSharpSourceParser.Parse(
            sources, availableMemoryBytes: 1024, processorCount: 16, ct: TestContext.Current.CancellationToken);
        var parallel = CSharpSourceParser.Parse(
            sources, availableMemoryBytes: 8L * 1024 * 1024 * 1024, processorCount: 16,
            ct: TestContext.Current.CancellationToken);

        Assert.Equal(sequential.Select(item => item.Path), parallel.Select(item => item.Path));
        var sequentialComplexity = ComplexityAnalyzer.Analyze(sequential);
        var parallelComplexity = ComplexityAnalyzer.Analyze(parallel);
        Assert.Equal(sequentialComplexity with { Hotspots = [] }, parallelComplexity with { Hotspots = [] });
        Assert.Equal(sequentialComplexity.Hotspots, parallelComplexity.Hotspots);
        Assert.Equal(
            ArchitectureAnalyzer.Analyze(sequential).Edges,
            ArchitectureAnalyzer.Analyze(parallel).Edges);
    }
}
