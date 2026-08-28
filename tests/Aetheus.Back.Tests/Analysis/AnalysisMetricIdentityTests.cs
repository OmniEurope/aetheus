// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Analysis;

namespace Aetheus.Back.Tests.Analysis;

public sealed class AnalysisMetricIdentityTests
{
    [Fact]
    public void Build_KeepsLocalRegressionsSeparateAndNormalizesPaths()
    {
        var first = AnalysisMetricIdentity.Build("complexity", "method", "CSharp", "src\\A.cs", "A.Run");
        var equivalent = AnalysisMetricIdentity.Build("COMPLEXITY", "METHOD", "csharp", "src/A.cs", "a.run");
        var otherSymbol = AnalysisMetricIdentity.Build("complexity", "method", "csharp", "src/A.cs", "B.Run");

        Assert.Equal(first, equivalent);
        Assert.NotEqual(first, otherSymbol);
    }
}
