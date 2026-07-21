// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Pages.Pipelines;
using Aetheus.Shared.DTOs;
using Bunit;

namespace Aetheus.Front.Tests.Pages.Pipelines;

public sealed class PipelineDefinitionPeekTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public PipelineDefinitionPeekTests() => _handler = BunitTestHelper.RegisterServices(this);

    [Fact]
    public void FuzzySearchResult_DoesNotDisplayDifferentPipeline()
    {
        _handler.SetJsonResponse("api/pipelines", new PaginatedResult<PipelineDto>
        {
            Items = [new PipelineDto { Id = 8, Name = "deploy-production", YamlDefinition = "wrong" }],
            TotalCount = 1,
            Page = 1,
            PageSize = 100
        });

        var cut = Render<PipelineDefinitionPeek>(parameters => parameters
            .Add(component => component.PipelineName, "deploy")
            .Add(component => component.ProjectId, 4));

        cut.WaitForAssertion(() => Assert.Contains("PipelineNotFound", cut.Markup));
        Assert.DoesNotContain(_handler.Requests, request =>
            request.Url.Contains("api/pipelines/8", StringComparison.Ordinal));
        Assert.Null(typeof(PipelineDefinitionPeek).GetField("_current",
            BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance));
    }

    [Fact]
    public void ExactCaseInsensitiveMatch_LoadsRequestedDefinition()
    {
        _handler.SetJsonResponse("api/pipelines", new PaginatedResult<PipelineDto>
        {
            Items = [new PipelineDto { Id = 9, Name = "Deploy", YamlDefinition = string.Empty }],
            TotalCount = 1,
            Page = 1,
            PageSize = 100
        });
        _handler.SetJsonResponse("api/pipelines/9", new PipelineDto
        {
            Id = 9,
            Name = "Deploy",
            YamlDefinition = "name: exact"
        });

        var cut = Render<PipelineDefinitionPeek>(parameters => parameters
            .Add(component => component.PipelineName, "deploy"));

        cut.WaitForAssertion(() => Assert.Contains("name: exact", cut.Markup));
        Assert.Contains(_handler.Requests, request =>
            request.Url.Contains("api/pipelines/9", StringComparison.Ordinal));
    }
}
