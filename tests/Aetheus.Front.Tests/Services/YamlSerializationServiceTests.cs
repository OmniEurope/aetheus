// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Tests;

public class YamlSerializationServiceTests
{
    private readonly YamlSerializationService _sut = new();

    [Fact]
    public void Parse_ValidYaml_ReturnsPipelineDefinition()
    {
        var yaml = @"stages:
  - name: build
    steps:
      - name: compile
        shell: bash";

        var result = _sut.Parse(yaml);
        Assert.NotNull(result);
        Assert.Single(result!.Stages);
        Assert.Equal("build", result.Stages[0].Name);
    }

    [Fact]
    public void Parse_EmptyYaml_ReturnsNull()
    {
        Assert.Null(_sut.Parse(""));
        Assert.Null(_sut.Parse("   "));
    }

    [Fact]
    public void Parse_InvalidYaml_ReturnsNull()
    {
        Assert.Null(_sut.Parse(":::invalid:::yaml{{{"));
    }

    [Fact]
    public void Parse_NullInput_ReturnsNull()
    {
        Assert.Null(_sut.Parse(null!));
    }

    [Fact]
    public void Serialize_DefinitionToYaml_ProducesValidOutput()
    {
        var def = new PipelineYamlDefinition
        {
            Stages =
            [
                new PipelineStageDefinition
                {
                    Name = "deploy",
                    Steps = [new PipelineStepDefinition { Name = "push", Shell = "bash" }]
                }
            ]
        };

        var yaml = _sut.Serialize(def);
        Assert.Contains("deploy", yaml);
        Assert.Contains("push", yaml);
    }

    [Fact]
    public void Roundtrip_SerializeAndParse_RetainData()
    {
        var def = new PipelineYamlDefinition
        {
            Stages =
            [
                new PipelineStageDefinition
                {
                    Name = "test",
                    Steps = [new PipelineStepDefinition { Name = "run-tests", Shell = "bash" }]
                }
            ]
        };

        var yaml = _sut.Serialize(def);
        var parsed = _sut.Parse(yaml);
        Assert.NotNull(parsed);
        Assert.Equal("test", parsed!.Stages[0].Name);
    }

    [Fact]
    public void Serialize_EmptyDefinition_ProducesYaml()
    {
        var def = new PipelineYamlDefinition();
        var yaml = _sut.Serialize(def);
        Assert.NotNull(yaml);
    }

    [Fact]
    public void Parse_YamlWithVariables_ParsesVariables()
    {
        var yaml = @"variables:
  MY_VAR: hello
stages: []";

        var result = _sut.Parse(yaml);
        Assert.NotNull(result);
        Assert.Equal("hello", result!.Variables["MY_VAR"]);
    }

    /// <summary>
    /// Computed properties never reach the YAML: the backend parses a saved pipeline strictly, and an
    /// `is_empty` or `is_container` key it does not know makes the whole definition invalid.
    /// </summary>
    [Fact]
    public void Serialize_LeavesComputedPropertiesOut()
    {
        var yaml = _sut.Serialize(new PipelineYamlDefinition
        {
            Name = "app",
            Requires = new PipelineRequiresDefinition { Libraries = ["app-host"] },
            Isolation = new PipelineIsolationDefinition { Mode = PipelineIsolationDefinition.ModeContainer }
        });

        Assert.Contains("app-host", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("is_empty", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("is_container", yaml, StringComparison.Ordinal);
    }
}
