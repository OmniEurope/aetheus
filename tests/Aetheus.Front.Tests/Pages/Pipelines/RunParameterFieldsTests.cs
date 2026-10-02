// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Components.Pipelines;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Aetheus.Front.Tests.Pages.Pipelines;

public sealed class RunParameterFieldsTests : BunitContext
{
    public RunParameterFieldsTests() => BunitTestHelper.RegisterServices(this);

    private static List<PipelineRunParameterDto> CandidateParameter() =>
        [new() { Name = "candidateVersion", DisplayName = "Candidate", Type = "string", Required = true }];

    private static List<ReleaseDto> TwoReleases() =>
        [
            new() { Id = 1, Version = "1.1.482", Status = ReleaseStatus.Published },
            new() { Id = 2, Version = "1.1.481", Status = ReleaseStatus.Promoted }
        ];

    /// <summary>
    /// The point of the release list: clicking a release fills the required version field, then the
    /// user still confirms it - the value goes through the same collection and validation as a typed one.
    /// </summary>
    [Fact]
    public void ClickingARelease_FillsTheRequiredVersionField()
    {
        var cut = Render<RunParameterFields>(p => p
            .Add(x => x.Parameters, CandidateParameter())
            .Add(x => x.AvailableReleases, TwoReleases()));

        // PLAN-005 lot 5 / D38: a row of the release grid fills the field.
        cut.WaitForAssertion(() => Assert.Contains("1.1.482", cut.Markup));
        cut.FindAll("tr[data-omni-row-index]").First(row => row.TextContent.Contains("1.1.482")).Click();

        Assert.True(cut.Instance.TryCollect(out var values, out var error));
        Assert.Null(error);
        Assert.Equal("1.1.482", values["candidateVersion"]);
    }

    /// <summary>
    /// With several required-without-default text fields there is no unambiguous target, so the list
    /// stays informational: no fill buttons, and an empty submit still fails on the required check.
    /// </summary>
    [Fact]
    public void AmbiguousTarget_ShowsTheListWithoutFillButtons()
    {
        var parameters = new List<PipelineRunParameterDto>
        {
            new() { Name = "candidateVersion", DisplayName = "Candidate", Type = "string", Required = true },
            new() { Name = "reason", DisplayName = "Reason", Type = "string", Required = true }
        };
        var cut = Render<RunParameterFields>(p => p
            .Add(x => x.Parameters, parameters)
            .Add(x => x.AvailableReleases, TwoReleases()));

        cut.WaitForAssertion(() => Assert.Contains("1.1.482", cut.Markup));
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Contains("1.1.482"));
        // A row click must not guess between the two fields.
        cut.FindAll("tr[data-omni-row-index]").First(row => row.TextContent.Contains("1.1.482")).Click();
        Assert.False(cut.Instance.TryCollect(out _, out var error));
        Assert.NotNull(error);
    }

    /// <summary>
    /// PLAN-005 lot 6 / D42: the description leaves the flow. It is carried by the help icon's
    /// tooltip (opened on hover and on keyboard focus) and by a hidden text the field points to
    /// through aria-describedby, so a screen reader still reads it with the field.
    /// </summary>
    [Fact]
    public void Description_IsInTheHelpTooltip_NotACaptionUnderTheField()
    {
        const string description = "Release to deploy, e.g. c-384f63...";
        var parameters = new List<PipelineRunParameterDto>
        {
            new() { Name = "candidateVersion", DisplayName = "Candidate", Type = "string", Required = true, Description = description },
            new() { Name = "dryRun", DisplayName = "Dry run", Type = "boolean", Description = "Plan only" },
            new() { Name = "plain", DisplayName = "Plain", Type = "string" }
        };
        var cut = Render<RunParameterFields>(p => p.Add(x => x.Parameters, parameters));

        Assert.DoesNotContain(cut.FindAll(".omni-text--caption"), caption => caption.TextContent.Contains(description));
        var help = cut.FindAll(".param-help");
        Assert.Equal(2, help.Count);
        Assert.Equal(description, help[0].GetAttribute("aria-label"));
        var tooltipTrigger = help[0].ParentElement!;
        Assert.Contains("omni-tooltip__trigger", tooltipTrigger.ClassList);
        Assert.Equal("0", tooltipTrigger.GetAttribute("tabindex"));

        var descriptionId = RunParameterFields.DescriptionId("candidateVersion", description)!;
        Assert.Equal(description, cut.Find($"#{descriptionId}").TextContent);
        Assert.Equal(descriptionId, cut.Find("input#candidateVersion").GetAttribute("aria-describedby"));
        Assert.Null(cut.Find("input#plain").GetAttribute("aria-describedby"));

        var tooltipId = tooltipTrigger.GetAttribute("aria-describedby");
        Assert.Equal(description, cut.Find($"#{tooltipId}").TextContent);
    }

    [Fact]
    public void NoReleases_RendersNoReleaseBlock()
    {
        var cut = Render<RunParameterFields>(p => p.Add(x => x.Parameters, CandidateParameter()));

        Assert.DoesNotContain("1.1.", cut.Markup);
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Contains("-"));
    }

    /// <summary>PLAN-003 D41: the French label and help when the interface is in French and the
    /// pipeline wrote them; the default ones otherwise, and always when it wrote none.</summary>
    [Theory]
    [InlineData("fr-FR", "Cible *", "Où cela part")]
    [InlineData("en-US", "Target *", "Where it goes")]
    public void TheLabelAndHelp_FollowTheInterfaceLanguage(string culture, string label, string help)
    {
        var previous = System.Globalization.CultureInfo.CurrentUICulture;
        System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo(culture);
        try
        {
            var cut = Render<RunParameterFields>(p => p.Add(x => x.Parameters,
            [
                new PipelineRunParameterDto
                {
                    Name = "target", DisplayName = "Target", DisplayNameFr = "Cible", Required = true,
                    Description = "Where it goes", DescriptionFr = "Où cela part"
                },
                new PipelineRunParameterDto { Name = "plain", DisplayName = "Plain" }
            ]));

            Assert.Contains(label, cut.Markup, StringComparison.Ordinal);
            Assert.Contains(help, cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Plain", cut.Markup, StringComparison.Ordinal);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentUICulture = previous;
        }
    }
}
