// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Layout;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Aetheus.Front.Tests.Shared;

public class BrowserBackButtonTests : BunitContext
{
    public BrowserBackButtonTests() => BunitTestHelper.RegisterServices(this);

    [Fact]
    public void Click_WithBreadcrumbParent_NavigatesToParentInsteadOfBrowserHistory()
    {
        var breadcrumb = Services.GetRequiredService<BreadcrumbService>();
        breadcrumb.Set(
            new BreadcrumbItem("Projects", "/projects"),
            new BreadcrumbItem("Toto", "/projects/42/overview"),
            new BreadcrumbItem("Pipelines", "/pipelines?projectId=42"),
            new BreadcrumbItem("Deploy"));

        var cut = Render<BrowserBackButton>();

        cut.Find("button").Click();

        var nav = Services.GetRequiredService<NavigationManager>();
        Assert.EndsWith("/pipelines?projectId=42", nav.Uri, StringComparison.Ordinal);
        Assert.DoesNotContain(JSInterop.Invocations, invocation => invocation.Identifier == "Aetheus.goBack");
    }

    [Fact]
    public void Click_WithoutBreadcrumbParent_UsesBrowserHistoryBack()
    {
        Services.GetRequiredService<BreadcrumbService>()
            .Set(new BreadcrumbItem("Current page"));
        JSInterop.SetupVoid("Aetheus.goBack");
        var cut = Render<BrowserBackButton>(parameters => parameters
            .Add(component => component.Text, "Back")
            .Add(component => component.Title, "Go back"));

        cut.Find("button").Click();

        JSInterop.VerifyInvoke("Aetheus.goBack");
    }
}
