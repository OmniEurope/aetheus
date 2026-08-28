// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Pages.Organizations;
using Aetheus.Shared.DTOs.Organizations;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Aetheus.Front.Tests.Pages;

public class OrganizationEditMethodTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public OrganizationEditMethodTests()
    {
        _handler = BunitTestHelper.RegisterServices(this, isAdmin: true);
    }

    private IRenderedComponent<OrganizationEdit> RenderNew()
    {
        return Render<OrganizationEdit>();
    }

    private IRenderedComponent<OrganizationEdit> RenderExisting()
    {
        _handler.SetJsonResponse("api/organizations/2",
            new OrganizationDto(2, "Acme", "acme", "Acme Corp", 5, 3, DateTime.UtcNow, DateTime.UtcNow));
        return Render<OrganizationEdit>(p => p.Add(x => x.Id, 2));
    }

    [Fact]
    public async Task OnSubmit_Create_CallsApi()
    {
        var cut = RenderNew();

        _handler.SetJsonResponse("api/organizations",
            new OrganizationDto(5, "New Org", "new-org", "", 0, 0, DateTime.UtcNow, DateTime.UtcNow));

        var modelField = typeof(OrganizationEdit).GetField("_model", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var model = (OrganizationFormModel)modelField.GetValue(cut.Instance)!;
        model.Name = "New Org";
        model.Slug = "new-org";

        var method = typeof(OrganizationEdit).GetMethod("OnSubmit", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance, [])!);

        // Create emits a POST to the collection endpoint (no id segment) and no PUT.
        Assert.Contains(_handler.Requests, r => r.Method == "POST" && r.Url.Contains("api/organizations"));
        Assert.DoesNotContain(_handler.Requests, r => r.Method == "PUT");
    }

    [Fact]
    public void LegacyEditRoute_RedirectsWithoutUpdateApi()
    {
        _ = RenderExisting();

        var nav = Services.GetRequiredService<NavigationManager>();
        Assert.EndsWith("/admin/organizations/2", nav.Uri);
        Assert.DoesNotContain(_handler.Requests, r => r.Method == "PUT");
        Assert.DoesNotContain(_handler.Requests, r => r.Method == "POST");
    }

    [Fact]
    public void Renders_New()
    {
        var cut = RenderNew();
        Assert.Contains("NewOrganization", cut.Markup);
    }

    [Fact]
    public void Renders_Existing()
    {
        var cut = RenderExisting();
        Assert.Contains("EditOrganization", cut.Markup);
    }
}
