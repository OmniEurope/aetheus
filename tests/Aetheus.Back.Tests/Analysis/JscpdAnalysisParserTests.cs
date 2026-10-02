// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Analysis;
using Aetheus.Back.Exceptions;

namespace Aetheus.Back.Tests.Analysis;

public sealed class JscpdAnalysisParserTests
{
    [Fact]
    public void Parse_SeparatesDuplicateFindingAndContinuousMetrics()
    {
        const string json = """
            {
              "statistics": {
                "total": {"lines":100,"clones":1,"duplicatedLines":10,"percentage":10.0},
                "formats": {"typescript":{"lines":80,"clones":1,"duplicatedLines":10,"percentage":12.5}}
              },
              "duplicates": [{
                "format":"typescript","lines":10,"fragment":"sensitive source omitted",
                "firstFile":{"name":"/src/a.ts","start":2,"end":11},
                "secondFile":{"name":"/src/b.ts","start":4,"end":13}
              }]
            }
            """;

        var report = JscpdAnalysisParser.Parse(json);

        var finding = Assert.Single(report.Findings);
        Assert.Equal(AnalysisCategory.Duplication, finding.Category);
        Assert.Equal("a.ts", finding.FilePath);
        Assert.DoesNotContain("sensitive", finding.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(report.Metrics, metric => metric.Key == "duplication.percentage"
            && metric.Language == "typescript" && metric.Value == 12.5);
        Assert.All(report.Metrics, metric => Assert.Equal("jscpd", metric.ToolName));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{")]
    public void Parse_RejectsInvalidShape(string json) =>
        Assert.Throws<BadRequestException>(() => JscpdAnalysisParser.Parse(json));
}
