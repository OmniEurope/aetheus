// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Analysis;
using Aetheus.Back.Exceptions;

namespace Aetheus.Back.Tests.Analysis;

public sealed class EslintAnalysisParserTests
{
    [Fact]
    public void Parse_NormalizesFindingsWithoutSourceContent()
    {
        const string json = """
            [{"filePath":"/src/web/app.ts","messages":[
              {"ruleId":"no-eval","severity":2,"message":"eval is forbidden","line":8,"endLine":8},
              {"ruleId":"eqeqeq","severity":1,"message":"Use strict equality","line":12}
            ]}]
            """;

        var findings = EslintAnalysisParser.Parse(json);

        Assert.Equal(2, findings.Count);
        Assert.Equal(AnalysisSeverity.Medium, findings[0].Severity);
        Assert.Equal("web/app.ts", findings[0].FilePath);
        Assert.Equal("no-eval", findings[0].RuleId);
        Assert.DoesNotContain("source", findings[0].Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[")]
    public void Parse_RejectsInvalidShape(string json) =>
        Assert.Throws<BadRequestException>(() => EslintAnalysisParser.Parse(json));
}
