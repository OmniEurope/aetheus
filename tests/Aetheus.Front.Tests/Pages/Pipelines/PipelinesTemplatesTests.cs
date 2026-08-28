// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Pages.Pipelines;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Aetheus.Front.Tests.Pages.Pipelines;

public class PipelinesTemplatesTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;
    private static readonly BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;

    public PipelinesTemplatesTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
    }

    [Fact]
    public void PipelinesPage_UnifiesUsedPipelinesAndModels()
    {
        _handler.SetJsonResponse("api/pipelines/dependencies", new PipelineDependencyGroupsDto());
        _handler.SetJsonResponse("api/pipelines/runs/recent", new List<PipelineRunDto>());
        _handler.SetJsonResponse("api/pipelines/favorites", new PipelineFavoritesDto());
        _handler.SetJsonResponse("api/pipelines/fleet", new PaginatedResult<PipelineFleetItemDto>());
        _handler.SetJsonResponse("api/pipelines/templates", new List<PipelineTemplateSummaryDto>());

        var cut = Render<Aetheus.Front.Pages.Pipelines.Pipelines>();

        Assert.Contains("UsedPipelines", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Templates", cut.Markup, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("templates", "/pipelines?tab=models")]
    [InlineData("pipelines/fleet", "/pipelines")]
    public void LegacyCatalogRoutes_RedirectToUnifiedHub(string legacyPath, string expectedPath)
    {
        var nav = Services.GetRequiredService<Bunit.TestDoubles.BunitNavigationManager>();
        nav.NavigateTo(legacyPath);

        Render<Aetheus.Front.Pages.Pipelines.Pipelines>();

        Assert.EndsWith(expectedPath, nav.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public void TemplatesPage_LoadsAndLinksDedicatedRoutes()
    {
        _handler.SetJsonResponse("api/pipelines/templates", new List<PipelineTemplateSummaryDto>
        {
            new()
            {
                Id = 7,
                Name = "Build Template",
                Category = "CI",
                Version = 3,
                UpdatedAt = new DateTime(2026, 8, 9, 10, 0, 0),
                PipelineCount = 4,
                LatestRunAt = new DateTime(2026, 8, 10, 8, 0, 0)
            }
        });

        var cut = Render<PipelineTemplates>();
        cut.WaitForState(() => cut.Markup.Contains("Build Template", StringComparison.Ordinal));

        Assert.Contains("href=\"/templates/7\"", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("v3", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("v@template.Version", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("UseTemplate", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("VersionHistoryTitle", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Category", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Updated", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("LatestRunShort", cut.Markup, StringComparison.Ordinal);
        Assert.Contains(">4<", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("href=\"/pipelines?templateId=7\"", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("title=\"ViewPipelinesUsingTemplate\"", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("title=\"UseTemplate\"", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("title=\"VersionHistoryTitle\"", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void TemplatesPage_LoadFailure_ShowsRetry()
    {
        _handler.SetResponse("api/pipelines/templates", System.Net.HttpStatusCode.ServiceUnavailable);

        var cut = Render<PipelineTemplates>();
        cut.WaitForAssertion(() => Assert.Contains("LoadFailed", cut.Markup, StringComparison.Ordinal));

        Assert.False((bool)typeof(PipelineTemplates).GetField("_loading", Priv)!.GetValue(cut.Instance)!);
        Assert.True((bool)typeof(PipelineTemplates).GetField("_loadFailed", Priv)!.GetValue(cut.Instance)!);
        Assert.Contains("Retry", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TemplatesPage_PermissionChangesHideWriteActions()
    {
        _handler.SetJsonResponse("api/pipelines/templates", new List<PipelineTemplateSummaryDto>());
        var cut = Render<PipelineTemplates>();
        Assert.Contains("NewTemplate", cut.Markup, StringComparison.Ordinal);

        var permissions = Services.GetRequiredService<PermissionService>();
        await cut.InvokeAsync(() => permissions.SetPermissions([], isAdmin: false));

        cut.WaitForAssertion(() => Assert.DoesNotContain("NewTemplate", cut.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public void NewTemplatePage_StartsWithUsableYamlAndInitialChangelog()
    {
        var cut = Render<PipelineTemplateEdit>();
        var model = typeof(PipelineTemplateEdit).GetField("_model", Priv)!.GetValue(cut.Instance)!;
        var modelType = model.GetType();

        Assert.Equal("Pipeline", modelType.GetProperty("Category")!.GetValue(model));
        Assert.Equal("InitialTemplateVersion", modelType.GetProperty("ChangelogEntry")!.GetValue(model));
        Assert.Contains("git status --short", (string)modelType.GetProperty("YamlContent")!.GetValue(model)!, StringComparison.Ordinal);
    }

    [Fact]
    public void NewTemplatePage_InputEventsAreSentByTheCreateForm()
    {
        _handler.SetJsonResponse(HttpMethod.Post, "api/pipelines/templates", new PipelineTemplateDto
        {
            Id = 42,
            Name = "Reusable validation",
            Category = "Validation",
            Version = 1
        });
        var cut = Render<PipelineTemplateEdit>();

        cut.Find("input[name='Name']").Input("Reusable validation");
        cut.Find("input[name='Category']").Input("Validation");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Contains(
            _handler.Requests,
            request => request.Method == "POST" && request.Url.EndsWith("api/pipelines/templates", StringComparison.Ordinal)));
        var body = _handler.RequestDetails.Last(request =>
            request.Method == "POST" && request.Url.EndsWith("api/pipelines/templates", StringComparison.Ordinal)).Body;
        var request = System.Text.Json.JsonSerializer.Deserialize<CreatePipelineTemplateRequest>(
            body!, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

        Assert.Equal("Reusable validation", request!.Name);
        Assert.Equal("Validation", request.Category);
        Assert.Contains("git status --short", request.YamlContent, StringComparison.Ordinal);
    }
}
