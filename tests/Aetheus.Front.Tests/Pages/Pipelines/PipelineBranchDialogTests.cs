// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Pages.Pipelines;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Bunit;

namespace Aetheus.Front.Tests.Pages.Pipelines;

public class PipelineBranchDialogTests : BunitContext
{
    public PipelineBranchDialogTests() => BunitTestHelper.RegisterServices(this);

    [Fact]
    public void Dialog_SelectsRepositoryDefaultAndRendersBranchContext()
    {
        var branches = new List<GitLightBranchDto>
        {
            new() { Name = "develop" },
            new() { Name = "main", IsDefault = true }
        };

        var cut = Render<PipelineBranchDialog>(parameters => parameters
            .Add(component => component.Branches, branches)
            .Add(component => component.SourceBranch, "develop"));

        var selected = typeof(PipelineBranchDialog)
            .GetField("_branch", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(cut.Instance);
        Assert.Equal("main", selected);
        Assert.Contains("RunFromBranch", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("BranchListRefreshedHint", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void OrderBranches_PutsRepositoryDefaultBeforeAlphabeticalBranches()
    {
        var ordered = PipelineRunDialogCoordinator.OrderBranches(
        [
            new GitLightBranchDto { Name = "release/next" },
            new GitLightBranchDto { Name = "develop" },
            new GitLightBranchDto { Name = "main", IsDefault = true }
        ],
        "main");

        Assert.Equal(["main", "develop", "release/next"], ordered.Select(branch => branch.Name));
    }
}
