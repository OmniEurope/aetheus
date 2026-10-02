// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Analysis;
using Aetheus.Back.Exceptions;

namespace Aetheus.Back.Tests.Analysis;

public sealed class AnalysisMetricParserTests
{
    [Fact]
    public void Parse_NormalizesFiniteMetrics()
    {
        const string json = """
            {"metrics":[{"key":"coverage.line.percent","value":82.5,"unit":"percent","scope":"project","language":"csharp","toolName":"Coverlet","direction":"HigherIsBetter"}]}
            """;

        var metric = Assert.Single(AnalysisMetricParser.Parse(json));

        Assert.Equal(82.5, metric.Value);
        Assert.Equal(AnalysisMetricDirection.HigherIsBetter, metric.Direction);
    }

    [Theory]
    [InlineData("{\"metrics\":{}}")]
    [InlineData("{\"metrics\":[{\"key\":\"x\",\"value\":\"NaN\",\"toolName\":\"t\"}]}")]
    public void Parse_RejectsMalformedReports(string json)
    {
        Assert.Throws<BadRequestException>(() => AnalysisMetricParser.Parse(json));
    }
}
