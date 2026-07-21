// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using PipelinesPage = Aetheus.Front.Pages.Pipelines.Pipelines;
namespace Aetheus.Front.Tests.Pages.Pipelines;

// 0-a: after the pipelines list moved to the shared PipelinesList component, the page only owns the
// Templates tab + tab selection. This covers that remaining page behavior; the pipelines list itself
// is covered by PipelinesListTests and the badge/trigger helpers by PipelineHelperTests.
public class PipelinesTemplatesTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;
    private static readonly BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly Type PageType = typeof(PipelinesPage);

    public PipelinesTemplatesTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
        // The default tab hosts the shared PipelinesList (global scope).
        _handler.SetJsonResponse("api/pipelines/dependencies", new PipelineDependencyGroupsDto());
        _handler.SetJsonResponse("api/pipelines/runs/recent", new List<PipelineRunDto>());
    }

    [Fact]
    public void Page_Renders_WithTabs()
    {
        var cut = Render<PipelinesPage>();
        // The page renders both tabs: the default Pipelines list and the Templates tab.
        Assert.Contains("Pipelines", cut.Markup);
        Assert.Contains("Templates", cut.Markup);
    }

    [Fact]
    public async Task TabChange_ToTemplates_LoadsTemplates()
    {
        _handler.SetJsonResponse("api/pipelines/templates", new List<PipelineTemplateSummaryDto>
        {
            new() { Id = 1, Name = "Build Template", Category = "CI" }
        });
        var cut = Render<PipelinesPage>();

        var method = PageType.GetMethod("OnTabChanged", Priv)!;
        await cut.InvokeAsync(() => (Task)method.Invoke(cut.Instance, [1])!);

        var templates = (List<PipelineTemplateSummaryDto>)PageType.GetField("_templates", Priv)!.GetValue(cut.Instance)!;
        Assert.Single(templates);
        Assert.True((bool)PageType.GetField("_templatesLoaded", Priv)!.GetValue(cut.Instance)!);
        var navigation = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        Assert.Equal(new Uri(new Uri(navigation.BaseUri), "templates"), new Uri(navigation.Uri));
    }

    [Fact]
    public async Task LoadTemplates_PopulatesTemplates()
    {
        _handler.SetJsonResponse("api/pipelines/templates", new List<PipelineTemplateSummaryDto>
        {
            new() { Id = 1, Name = "T1" }, new() { Id = 2, Name = "T2" }
        });
        var cut = Render<PipelinesPage>();

        var method = PageType.GetMethod("LoadTemplates", Priv)!;
        await cut.InvokeAsync(() => (Task)method.Invoke(cut.Instance, [])!);

        var templates = (List<PipelineTemplateSummaryDto>)PageType.GetField("_templates", Priv)!.GetValue(cut.Instance)!;
        Assert.Equal(2, templates.Count);
    }

    [Fact]
    public async Task LoadTemplates_Failure_ClearsSpinnerAndShowsRetry()
    {
        _handler.SetResponse("api/pipelines/templates", System.Net.HttpStatusCode.ServiceUnavailable);
        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>()
            .NavigateTo("http://test/templates");
        var cut = Render<PipelinesPage>();

        var method = PageType.GetMethod("LoadTemplates", Priv)!;
        await cut.InvokeAsync(() => (Task)method.Invoke(cut.Instance, [])!);
        PageType.GetField("_selectedTab", Priv)!.SetValue(cut.Instance, 1);
        cut.Render();

        Assert.False((bool)PageType.GetField("_templatesLoading", Priv)!.GetValue(cut.Instance)!);
        Assert.True((bool)PageType.GetField("_templatesLoadFailed", Priv)!.GetValue(cut.Instance)!);
        Assert.Contains("LoadFailed", cut.Markup);
        Assert.Contains("Retry", cut.Markup);
    }

    [Fact]
    public async Task PermissionChanges_RecomputeTemplateWriteActions()
    {
        var cut = Render<PipelinesPage>();
        Assert.True((bool)PageType.GetField("_canTemplateWrite", Priv)!.GetValue(cut.Instance)!);

        var permissions = Services.GetRequiredService<PermissionService>();
        await cut.InvokeAsync(() => permissions.SetPermissions([], isAdmin: false));

        cut.WaitForAssertion(() =>
            Assert.False((bool)PageType.GetField("_canTemplateWrite", Priv)!.GetValue(cut.Instance)!));
        Assert.DoesNotContain("New Template", cut.Markup, StringComparison.Ordinal);
    }
}
