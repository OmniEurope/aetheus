// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Pages.Organizations;
using Aetheus.Front.Tests.TestDoubles;
using Aetheus.Shared.DTOs.Organizations;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Radzen;

namespace Aetheus.Front.Tests.Pages.Organizations;

/// <summary>
/// Behavioural tests for OrganizationDialog.razor.cs: create mode starts blank and POSTs to the
/// collection endpoint; edit mode prefills the model from the parameter and PUTs to the id endpoint.
/// The dialog-close payload is captured by a spy DialogService.
/// </summary>
public class OrganizationDialogTests : BunitContext
{
    private static readonly BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;
    private readonly BunitTestHelper.TestHandler _handler;

    public OrganizationDialogTests()
    {
        _handler = BunitTestHelper.RegisterServices(this, authenticated: true, isAdmin: true);
    }

    private void RegisterSpyDialog() =>
        Services.AddSingleton<DialogService>(sp => new SpyDialogService(
            sp.GetRequiredService<NavigationManager>(),
            sp.GetRequiredService<IJSRuntime>()));

    private SpyDialogService Spy() => (SpyDialogService)Services.GetRequiredService<DialogService>();

    private static OrganizationDto MakeOrg(int id = 5) =>
        new(id, "Acme Corp", "acme", "The Acme organization", 3, 2, DateTime.UtcNow, DateTime.UtcNow);

    private static CreateOrganizationRequest Model(IRenderedComponent<OrganizationDialog> cut) =>
        (CreateOrganizationRequest)typeof(OrganizationDialog).GetField("_model", Priv)!.GetValue(cut.Instance)!;

    private static bool IsNew(IRenderedComponent<OrganizationDialog> cut) =>
        (bool)typeof(OrganizationDialog).GetProperty("_isNew", Priv)!.GetValue(cut.Instance)!;

    // ── Create vs edit distinction ───────────────────────────────────────────

    [Fact]
    public void CreateMode_StartsBlank_AndIsNew()
    {
        var cut = Render<OrganizationDialog>();

        Assert.True(IsNew(cut));
        // No Organization parameter ⇒ empty model fields.
        Assert.Equal(string.Empty, Model(cut).Name);
        Assert.Equal(string.Empty, Model(cut).Slug);
        // The dialog reuses a single "Save" submit button in both modes; the create/edit
        // distinction lives in _isNew + the blank model (asserted above), not the label.
        Assert.Contains("Save", cut.Markup);
    }

    [Fact]
    public void EditMode_PrefillsModelFromParameter()
    {
        var cut = Render<OrganizationDialog>(p => p.Add(c => c.Organization, MakeOrg()));

        Assert.False(IsNew(cut));
        // OnInitialized copied the existing org into the editable model.
        Assert.Equal("Acme Corp", Model(cut).Name);
        Assert.Equal("acme", Model(cut).Slug);
        Assert.Equal("The Acme organization", Model(cut).Description);
    }

    // ── Submit routes to the right verb/endpoint ─────────────────────────────

    [Fact]
    public async Task OnSubmit_CreateMode_PostsToCollection_AndClosesWithDto()
    {
        RegisterSpyDialog();
        _handler.SetJsonResponse(HttpMethod.Post, "api/organizations", MakeOrg(id: 11));

        var cut = Render<OrganizationDialog>();
        Model(cut).Name = "New Org";
        Model(cut).Slug = "new-org";

        await cut.InvokeAsync(async () =>
            await (Task)typeof(OrganizationDialog).GetMethod("OnSubmit", Priv)!.Invoke(cut.Instance, [])!);

        // Create uses POST on the collection endpoint (no id segment).
        Assert.Contains(_handler.Requests, r => r.Method == "POST" && r.Url.Contains("api/organizations"));
        Assert.DoesNotContain(_handler.Requests, r => r.Method == "PUT");
        Assert.True(Spy().Closed);
        Assert.IsType<OrganizationDto>(Spy().LastResult);
    }

    [Fact]
    public async Task OnSubmit_EditMode_PutsToIdEndpoint_AndClosesWithDto()
    {
        RegisterSpyDialog();
        _handler.SetJsonResponse(HttpMethod.Put, "api/organizations/5", MakeOrg(id: 5));

        var cut = Render<OrganizationDialog>(p => p.Add(c => c.Organization, MakeOrg(id: 5)));
        Model(cut).Name = "Acme Renamed";

        await cut.InvokeAsync(async () =>
            await (Task)typeof(OrganizationDialog).GetMethod("OnSubmit", Priv)!.Invoke(cut.Instance, [])!);

        // Edit uses PUT against the id endpoint.
        Assert.Contains(_handler.Requests, r => r.Method == "PUT" && r.Url.Contains("api/organizations/5"));
        Assert.DoesNotContain(_handler.Requests, r => r.Method == "POST");
        Assert.True(Spy().Closed);
        Assert.IsType<OrganizationDto>(Spy().LastResult);
    }

    [Fact]
    public async Task OnSubmit_NullResult_SetsErrorAndStaysOpen()
    {
        RegisterSpyDialog();
        _handler.SetResponse(HttpMethod.Post, "api/organizations", System.Net.HttpStatusCode.Conflict);

        var cut = Render<OrganizationDialog>();
        Model(cut).Name = "Dup";
        Model(cut).Slug = "dup";

        await cut.InvokeAsync(async () =>
            await (Task)typeof(OrganizationDialog).GetMethod("OnSubmit", Priv)!.Invoke(cut.Instance, [])!);

        Assert.False(Spy().Closed);
        var error = (string?)typeof(OrganizationDialog).GetField("_error", Priv)!.GetValue(cut.Instance);
        Assert.False(string.IsNullOrEmpty(error));
    }
}
