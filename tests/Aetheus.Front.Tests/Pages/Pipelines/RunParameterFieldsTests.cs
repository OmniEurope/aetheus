// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Pages.Pipelines;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Bunit;

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

        cut.FindAll("button").First(b => b.TextContent.Contains("1.1.482")).Click();

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

        Assert.Contains("1.1.482", cut.Markup);
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Contains("1.1.482"));
        Assert.False(cut.Instance.TryCollect(out _, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void NoReleases_RendersNoReleaseBlock()
    {
        var cut = Render<RunParameterFields>(p => p.Add(x => x.Parameters, CandidateParameter()));

        Assert.DoesNotContain("1.1.", cut.Markup);
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Contains("-"));
    }
}
