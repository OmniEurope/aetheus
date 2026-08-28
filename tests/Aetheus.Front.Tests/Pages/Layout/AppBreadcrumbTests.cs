// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Layout;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Aetheus.Front.Tests.Pages.Layout;

public sealed class AppBreadcrumbTests : BunitContext
{
    public AppBreadcrumbTests() => BunitTestHelper.RegisterServices(this);

    [Fact]
    public void Renders_Accessible_Global_Breadcrumb_For_Current_Route()
    {
        var nav = Services.GetRequiredService<Bunit.TestDoubles.BunitNavigationManager>();
        nav.NavigateTo("/pipelines/runs/1312");

        var cut = Render<AppBreadcrumb>();

        var landmark = cut.Find("nav[data-testid='app-breadcrumb']");
        Assert.Equal("BreadcrumbNavigation", landmark.GetAttribute("aria-label"));
        Assert.Equal("OL", landmark.FirstElementChild?.TagName);
        Assert.Contains("Pipelines", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("PipelineRun #1312", cut.Markup, StringComparison.Ordinal);
        var skeleton = cut.Find(".app-breadcrumb-skeleton");
        Assert.Equal("Loading", skeleton.GetAttribute("aria-label"));
        Assert.Equal("page", skeleton.GetAttribute("aria-current"));
        Assert.Equal("/pipelines", cut.Find("a").GetAttribute("href"));
    }

    [Fact]
    public void Navigation_Replaces_Old_Items_With_Destination_Fallback_Immediately()
    {
        var nav = Services.GetRequiredService<Bunit.TestDoubles.BunitNavigationManager>();
        nav.NavigateTo("/tasks/9");
        var cut = Render<AppBreadcrumb>();
        var breadcrumb = Services.GetRequiredService<BreadcrumbService>();
        breadcrumb.Set(new BreadcrumbItem("Tasks", "/tasks"), new BreadcrumbItem("Loaded task"));

        nav.NavigateTo("/servers/7/docker");

        cut.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("Loaded task", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("Server #7", cut.Markup, StringComparison.Ordinal);
            Assert.Single(cut.FindAll(".app-breadcrumb-skeleton"));
            Assert.Contains("Docker", cut.Markup, StringComparison.Ordinal);
        });

        breadcrumb.Set(
            new BreadcrumbItem("Servers", "/servers"),
            new BreadcrumbItem("web-01", "/servers/7/overview"),
            new BreadcrumbItem("Docker"));

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll(".app-breadcrumb-skeleton"));
            Assert.Contains("web-01", cut.Markup, StringComparison.Ordinal);
        });
    }
}
