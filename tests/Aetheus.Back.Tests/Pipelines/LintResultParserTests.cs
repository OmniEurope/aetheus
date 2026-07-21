// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Pipelines;

namespace Aetheus.Back.Tests.Pipelines;

public class LintResultParserTests
{
    [Fact]
    public void Parse_ValidSarif_CountsByLevelAndExtractsTool()
    {
        var sarif = """
            {
              "version": "2.1.0",
              "runs": [
                {
                  "tool": { "driver": { "name": "ESLint" } },
                  "results": [
                    { "level": "error", "message": { "text": "a" } },
                    { "level": "error", "message": { "text": "b" } },
                    { "level": "warning", "message": { "text": "c" } },
                    { "level": "note", "message": { "text": "d" } }
                  ]
                }
              ]
            }
            """;

        var result = LintResultParser.Parse(sarif, 7, "lint", "eslint");

        Assert.NotNull(result);
        Assert.Equal(7, result!.PipelineRunId);
        Assert.Equal("lint", result.StageName);
        Assert.Equal("eslint", result.StepName);
        Assert.Equal("ESLint", result.Tool);
        Assert.Equal(2, result.ErrorCount);
        Assert.Equal(1, result.WarningCount);
        Assert.Equal(1, result.InfoCount);
    }

    [Fact]
    public void Parse_AbsentLevel_DefaultsToWarning()
    {
        var sarif = """
            { "runs": [ { "results": [ { "message": { "text": "x" } } ] } ] }
            """;

        var result = LintResultParser.Parse(sarif, 1, null, null);

        Assert.NotNull(result);
        Assert.Equal(0, result!.ErrorCount);
        Assert.Equal(1, result.WarningCount);
    }

    [Fact]
    public void Parse_NoResults_ReturnsZeroCounts()
    {
        var sarif = """{ "runs": [ { "tool": { "driver": { "name": "roslyn" } } } ] }""";

        var result = LintResultParser.Parse(sarif, 1, null, null);

        Assert.NotNull(result);
        Assert.Equal("roslyn", result!.Tool);
        Assert.Equal(0, result.ErrorCount);
        Assert.Equal(0, result.WarningCount);
        Assert.Equal(0, result.InfoCount);
    }

    [Fact]
    public void Parse_NoRunsProperty_ReturnsNull()
    {
        var result = LintResultParser.Parse("""{ "version": "2.1.0" }""", 1, null, null);

        Assert.Null(result);
    }

    [Fact]
    public void Parse_MalformedJson_ReturnsNull()
    {
        var result = LintResultParser.Parse("not json {{", 1, null, null);

        Assert.Null(result);
    }
}
