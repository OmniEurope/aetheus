// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Pipelines;
using Aetheus.Back.Services;

namespace Aetheus.Back.Tests.Pipelines;

/// <summary>
/// <c>outputs:</c> is a step field that widens what the launch check accepts (D-02). It must parse
/// through the strict deserializer every definition goes through, and it must be held to the shape of
/// a published name: a lower-case or misspelled entry would exempt a reference nothing will satisfy.
/// </summary>
public class PipelineStepOutputsDeclarationTests
{
    private static List<string> Errors(params string[] outputs)
    {
        var definition = new PipelineYamlDefinition
        {
            Name = "deploy",
            Stages =
            [
                new PipelineStageDefinition
                {
                    Name = "Prepare",
                    Agent = "linux-01",
                    Steps = [new PipelineStepDefinition { Name = "prepare", Shell = "sh prepare.sh", Outputs = [.. outputs] }]
                }
            ]
        };
        var errors = new List<string>();
        PipelineDefinitionValidator.ValidateStages(definition, errors, []);
        return errors;
    }

    [Fact]
    public void TheYamlField_ParsesThroughTheStrictDeserializer()
    {
        const string yaml = """
            name: deploy
            stages:
              - name: Prepare
                steps:
                  - name: prepare
                    shell: sh deploy/scripts/prod-deploy-prepare.sh
                    outputs: [SOURCE_COMMIT, AETHEUS_BACK_IMAGE]
            """;

        var definition = YamlParsingHelper.Deserializer.Deserialize<PipelineYamlDefinition>(yaml);

        Assert.Equal(["SOURCE_COMMIT", "AETHEUS_BACK_IMAGE"], definition.Stages[0].Steps[0].Outputs);
    }

    [Fact]
    public void UpperSnakeNames_AreAccepted()
        => Assert.Empty(Errors("SOURCE_COMMIT", "AETHEUS_BACK_IMAGE"));

    [Theory]
    [InlineData("source_commit")]
    [InlineData("SOURCE-COMMIT")]
    [InlineData("1ST")]
    [InlineData("")]
    public void AnythingElse_IsRefused(string output)
    {
        var error = Assert.Single(Errors(output));
        Assert.Contains("upper snake case", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ADuplicate_IsRefused()
        => Assert.Contains(Errors("SOURCE_COMMIT", "SOURCE_COMMIT"), error => error.Contains("twice", StringComparison.Ordinal));

    [Fact]
    public void TheListIsBounded()
    {
        var outputs = Enumerable.Range(0, PipelineUnresolvedVariableGuard.MaxDeclaredOutputs + 1)
            .Select(index => $"OUTPUT_{index}")
            .ToArray();

        Assert.Contains(Errors(outputs), error => error.Contains("more than", StringComparison.Ordinal));
    }
}
