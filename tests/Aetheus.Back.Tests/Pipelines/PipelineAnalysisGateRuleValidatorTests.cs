// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Services;
using Aetheus.Shared.Analysis;
using Aetheus.Shared.DTOs;

namespace Aetheus.Back.Tests.Pipelines;

public sealed class PipelineAnalysisGateRuleValidatorTests
{
    [Fact]
    public void Validate_AcceptsCompactQualityOverrides()
    {
        var rules = new List<PipelineAnalysisGateRuleDefinition>
        {
            new() { Key = "quality.coverage.line", Minimum = 82, Behavior = "block" },
            new() { Key = "quality.duplication.percentage", Maximum = 4, Behavior = "warn" }
        };

        var errors = PipelineAnalysisGateRuleValidator.Validate("quality", "strict", rules);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_RejectsAmbiguousAndDuplicateRules()
    {
        var rules = new List<PipelineAnalysisGateRuleDefinition>
        {
            new() { Key = "quality.coverage.line", Minimum = 80, Maximum = 90 },
            new() { Key = "quality.coverage.line", Behavior = "ignore" }
        };

        var errors = PipelineAnalysisGateRuleValidator.Validate("quality", null, rules);

        Assert.Contains(errors, error => error.Contains("both minimum and maximum", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("declared more than once", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("behavior must be warn or block", StringComparison.Ordinal));
    }

    [Fact]
    public void ParseAndValidate_DeserializesFriendlyYamlContract()
    {
        const string yaml = """
            stages:
              - name: Analyze
                steps:
                  - name: Quality gate
                    type: analysis-gate
                    analysis_scope: quality
                    analysis_preset: recommended
                    analysis_rules:
                      - key: quality.coverage.line
                        minimum: 82
                        behavior: block
            """;

        var definition = YamlParsingHelper.ParseAndValidate(yaml);
        var gate = Assert.Single(Assert.Single(definition!.Stages).Steps);

        Assert.Equal("recommended", gate.AnalysisPreset);
        var rule = Assert.Single(gate.AnalysisRules);
        Assert.Equal("quality.coverage.line", rule.Key);
        Assert.Equal(82, rule.Minimum);
    }
}
