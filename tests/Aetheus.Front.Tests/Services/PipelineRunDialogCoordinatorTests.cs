// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Services;
using Aetheus.Front.Tests.TestDoubles;
using Aetheus.Shared.DTOs;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Radzen;

namespace Aetheus.Front.Tests.Services;

public sealed class PipelineRunDialogCoordinatorTests : BunitContext
{
    [Fact]
    public async Task ChooseBranchAsync_RefreshesBranchesAndSelectsRepositoryDefault()
    {
        var handler = BunitTestHelper.RegisterServices(this);
        BunitTestHelper.UseImmediateDialogs(this);
        handler.SetJsonResponse("api/pipelines/5/source", new PipelineSourceDto
        {
            RepositoryId = 9,
            Branch = "develop"
        });
        handler.SetJsonResponse(
            HttpMethod.Get,
            "api/git/repos/9",
            new GitLightRepoDto { Id = 9, Name = "app", DefaultBranch = "main" });
        handler.SetPaginatedJsonResponse(
            HttpMethod.Get,
            "api/git/repos/9/branches",
            new List<GitLightBranchDto>
            {
                new() { Name = "develop" },
                new() { Name = "main", IsDefault = true }
            });

        var dialog = (ImmediateDialogService)Services.GetRequiredService<DialogService>();
        await Services.GetRequiredService<PipelineRunDialogCoordinator>().ChooseBranchAsync(5);
        var openedParameters = dialog.LastParameters;

        Assert.NotNull(openedParameters);
        Assert.Equal("main", openedParameters["SourceBranch"]);
        var branches = Assert.IsType<List<GitLightBranchDto>>(openedParameters["Branches"]);
        Assert.Collection(
            branches,
            branch =>
            {
                Assert.Equal("main", branch.Name);
                Assert.True(branch.IsDefault);
            },
            branch => Assert.Equal("develop", branch.Name));
        Assert.Contains(handler.Requests, request =>
            request.Method == "GET"
            && request.Url.Contains("api/git/repos/9/branches", StringComparison.Ordinal)
            && request.Url.Contains("page=1", StringComparison.Ordinal)
            && request.Url.Contains("pageSize=100", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CollectParametersAsync_RequestFailure_BlocksPipelineLaunch()
    {
        var handler = BunitTestHelper.RegisterServices(this);
        BunitTestHelper.UseImmediateDialogs(this);
        handler.SetResponse("api/pipelines/5/parameters", System.Net.HttpStatusCode.BadGateway);

        var result = await Services.GetRequiredService<PipelineRunDialogCoordinator>()
            .CollectParametersAsync(5);

        Assert.False(result.Proceed);
        Assert.Null(result.Parameters);
    }
}
