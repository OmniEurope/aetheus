// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Services;

namespace Aetheus.Back.Tests.Pipelines;

public sealed class PipelineAnalysisGradingValidatorTests
{
    [Fact]
    public void Validate_AcceptsVersionedCoverageBands()
    {
        var grading = CoverageGrading();

        var errors = PipelineAnalysisGradingValidator.Validate("quality", grading);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_RejectsNonMonotonicBandsAndMissingRequiredDomainRule()
    {
        var grading = CoverageGrading() with
        {
            RequiredDomains = ["reliability", "architecture"],
            Rules =
            [
                CoverageGrading().Rules[0] with
                {
                    A = 75,
                    B = 80
                }
            ]
        };

        var errors = PipelineAnalysisGradingValidator.Validate("quality", grading);

        Assert.Contains(errors, error => error.Contains("not monotonic", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains(
            "required domain 'architecture' has no grading rule",
            StringComparison.Ordinal));
    }

    [Fact]
    public void ParseAndValidate_DeserializesAnalysisGrading()
    {
        const string yaml = """
            stages:
              - name: Analyze
                steps:
                  - name: Quality gate
                    type: analysis-gate
                    analysis_scope: quality
                    analysis_grading:
                      version: 1
                      minimum_grade: C
                      required_domains: [reliability]
                      rules:
                        - key: grade.coverage.line
                          domain: reliability
                          metric: coverage.line.percent
                          category: coverage
                          direction: higher-is-better
                          a: 75
                          b: 65
                          c: 55
                          d: 40
                          e: 20
            """;

        var definition = YamlParsingHelper.ParseAndValidate(yaml);
        var grading = Assert.Single(Assert.Single(definition!.Stages).Steps).AnalysisGrading;

        Assert.NotNull(grading);
        Assert.Equal("C", grading.MinimumGrade);
        Assert.Equal(["reliability"], grading.RequiredDomains);
        Assert.Equal(75, Assert.Single(grading.Rules).A);
    }

    private static PipelineAnalysisGradingDefinition CoverageGrading() => new()
    {
        MinimumGrade = "C",
        RequiredDomains = ["reliability"],
        Rules =
        [
            new PipelineAnalysisGradeRuleDefinition
            {
                Key = "grade.coverage.line",
                Domain = "reliability",
                Metric = "coverage.line.percent",
                Category = "coverage",
                Direction = "higher-is-better",
                A = 75,
                B = 65,
                C = 55,
                D = 40,
                E = 20
            }
        ]
    };
}
