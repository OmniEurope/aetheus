// SPDX-License-Identifier: EUPL-1.2
using Bunit;

namespace Aetheus.Front.Tests.Shared;

public sealed class QualityGatePolicyEditorTests : BunitContext
{
    public QualityGatePolicyEditorTests() => BunitTestHelper.RegisterServices(this);

    [Fact]
    public void ProjectView_DistinguishesInheritedAndEditableRules()
    {
        var policies = new List<AnalysisPolicyDto>
        {
            new()
            {
                Id = -11,
                PolicyKey = "quality.coverage.line",
                Name = "quality.coverage.line",
                MetricKey = "coverage.line.percent",
                Operator = AnalysisPolicyOperator.LessThan,
                Threshold = 75,
                Scope = AnalysisPolicyScope.System,
                IsInherited = true,
                IsEffective = true,
                Enabled = true,
                Version = 1
            },
            new()
            {
                Id = 20,
                ProjectId = 4,
                PolicyKey = "project.no-cycles",
                Name = "No cycles",
                MetricKey = "architecture.cycles*",
                Operator = AnalysisPolicyOperator.GreaterThan,
                Threshold = 0,
                Scope = AnalysisPolicyScope.Project,
                IsEffective = true,
                Enabled = true,
                Version = 2
            }
        };

        var cut = Render<QualityGatePolicyEditor>(parameters => parameters
            .Add(component => component.Scope, AnalysisPolicyScope.Project)
            .Add(component => component.ProjectId, 4)
            .Add(component => component.CanEdit, true)
            .Add(component => component.InitialPolicies, policies));

        cut.WaitForState(() => cut.Markup.Contains("quality.coverage.line"), TimeSpan.FromSeconds(3));
        Assert.Contains("QualityGateScopeSystem", cut.Markup);
        Assert.Contains("QualityGateScopeProject", cut.Markup);
        Assert.Single(cut.FindAll("button[title='QualityGateOverride']"));
        Assert.Single(cut.FindAll("button[title='Edit']"));
        Assert.Single(cut.FindAll("button[title='QualityGateHistory']"));
        Assert.Equal(2, cut.FindAll("button[title='Duplicate']").Count);
        Assert.Contains("QualityGateRecommended", cut.Markup);
        Assert.Contains("QualityGateStrict", cut.Markup);
        Assert.Contains("QualityGateOptionalTitle", cut.Markup);
        Assert.NotNull(cut.Find(".omni-code-block.quality-gate-yaml-panel figcaption button")); // R-516: the YAML example and its copy button
        Assert.Contains("analysis_preset: recommended", cut.Markup);
        Assert.Contains(">Import<", cut.Markup, StringComparison.Ordinal);
        Assert.Contains(">Export<", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void RuleForm_ExposesAccessibleNamesForEveryInput()
    {
        var cut = Render<QualityGatePolicyEditor>(parameters => parameters
            .Add(component => component.Scope, AnalysisPolicyScope.Global)
            .Add(component => component.CanEdit, true)
            .Add(component => component.InitialPolicies, []));

        cut.FindAll("button")
            .Single(button => button.Names().Contains("QualityGateAddRule", StringComparison.Ordinal))
            .Click();

        var expectedLabels = new[]
        {
            "QualityGateRuleType",
            "Name",
            "QualityGatePolicyKey",
            "Category",
            "AnalysisScanner",
            "AnalysisRule",
            "Severity",
            "Branch",
            "Environment",
            "Behavior",
            "Priority"
        };

        var labelsWithInvalidCount = expectedLabels
            .Where(label => cut.FindAll($"[aria-label='{label}']").Count != 1)
            .ToArray();
        Assert.Empty(labelsWithInvalidCount);

        Assert.Single(cut.FindAll("input[aria-label='Priority']"));
    }
}
