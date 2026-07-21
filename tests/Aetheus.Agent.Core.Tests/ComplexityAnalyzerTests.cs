// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Agent.Core.Operations;

namespace Aetheus.Agent.Core.Tests;

public class ComplexityAnalyzerTests
{
    [Fact]
    public void Analyze_ComputesPerMethodCyclomaticComplexity()
    {
        const string src = """
            namespace X;
            public class C
            {
                public int Simple() => 1;
                public int Branchy(int n)
                {
                    if (n > 0 && n < 10) { }
                    for (var i = 0; i < n; i++) { }
                    switch (n) { case 1: break; case 2: break; default: break; }
                    return n;
                }
            }
            """;

        var report = ComplexityAnalyzer.Analyze([("C.cs", src)]);

        // Simple = 1; Branchy = 1 + if + && + for + case 1 + case 2 = 6 (default label does not count).
        Assert.Equal(2, report.TotalMethods);
        Assert.Equal(6, report.MaxCyclomatic);
        Assert.Equal(3.5, report.AvgCyclomatic, 2);
        Assert.Equal(0, report.HighComplexityMethods);
        Assert.True(report.TotalLinesOfCode > 0);
    }

    [Fact]
    public void Analyze_FlagsMethodsOverThreshold()
    {
        // A method with 12 if-statements → CC 13 (> 10 threshold).
        var ifs = string.Join("\n", Enumerable.Range(0, 12).Select(i => $"if (n == {i}) {{ }}"));
        var src = $$"""
            public class C
            {
                public void Hot(int n)
                {
                    {{ifs}}
                }
            }
            """;

        var report = ComplexityAnalyzer.Analyze([("C.cs", src)]);

        Assert.Equal(1, report.TotalMethods);
        Assert.Equal(13, report.MaxCyclomatic);
        Assert.Equal(1, report.HighComplexityMethods);
    }

    [Fact]
    public void Analyze_NoMethods_ReturnsZeroComplexityButCountsLoc()
    {
        const string src = """
            namespace X;
            public class C
            {
                public int Value;
            }
            """;

        var report = ComplexityAnalyzer.Analyze([("C.cs", src)]);

        Assert.Equal(0, report.TotalMethods);
        Assert.Equal(0, report.MaxCyclomatic);
        Assert.Equal(0, report.AvgCyclomatic);
        Assert.True(report.TotalLinesOfCode > 0);
    }
}
