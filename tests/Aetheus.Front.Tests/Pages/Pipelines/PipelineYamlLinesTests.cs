// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Components.Pipelines;
namespace Aetheus.Front.Tests.Pages;

public sealed class PipelineYamlLinesTests
{
    [Theory]
    [InlineData("pipeline: child", "pipeline: ", "child")]
    [InlineData("  pipeline: 'child-one'", "  pipeline: ", "child-one")]
    [InlineData("  - pipeline: \"child-two\"", "  - pipeline: ", "child-two")]
    public void Parse_RecognizesPipelineReferencesAndRemovesQuotes(
        string yaml,
        string expectedPrefix,
        string expectedReference)
    {
        var line = Assert.Single(PipelineYamlLines.Parse(yaml));

        Assert.Equal(expectedPrefix, line.Text);
        Assert.Equal(expectedReference, line.PipelineRef);
    }

    [Theory]
    [InlineData("pipeline_name: child")]
    [InlineData("# pipeline: child")]
    [InlineData("pipeline: child # comments are not part of a reference")]
    [InlineData("script: echo pipeline: child")]
    public void Parse_NonReference_PreservesOriginalLine(string yaml)
    {
        var line = Assert.Single(PipelineYamlLines.Parse(yaml));

        Assert.Equal(yaml, line.Text);
        Assert.Null(line.PipelineRef);
    }

    [Fact]
    public void Parse_NormalizesWindowsLineEndingsAndPreservesBlankLines()
    {
        var lines = PipelineYamlLines.Parse("name: parent\r\npipeline: child\r\n");

        Assert.Equal(3, lines.Count);
        Assert.Equal("child", lines[1].PipelineRef);
        Assert.Equal(string.Empty, lines[2].Text);
    }
}
