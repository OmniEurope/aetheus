// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Components.Dashboards;
using Aetheus.Front.Tests.TestDoubles;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace Aetheus.Front.Tests.Pages.Dashboard;

public class DashboardsDeepTests : BunitContext
{
    private static readonly BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;
    private readonly BunitTestHelper.TestHandler _handler;

    public DashboardsDeepTests()
    {
        _handler = BunitTestHelper.RegisterServices(this, isAdmin: true);
        Services.AddSingleton<OmniDialogService>(sp => new SpyDialogService(
            sp.GetRequiredService<NavigationManager>(), sp.GetRequiredService<IJSRuntime>()));
    }

    private SpyDialogService Spy() => (SpyDialogService)Services.GetRequiredService<OmniDialogService>();

    private void SetupDashboards(List<DashboardDto>? list = null)
    {
        _handler.SetJsonResponse("api/dashboards", list ?? [
            new DashboardDto { Id = 1, Name = "Operations", Widgets = [] },
            new DashboardDto { Id = 2, Name = "Infra", Widgets = [] }
        ]);
    }

    [Fact]
    public void Renders_DashboardList_WhenAdmin()
    {
        SetupDashboards();
        var cut = Render<Dashboards>();
        cut.WaitForState(() => cut.Markup.Contains("Operations") || !cut.Markup.Contains("rz-progressbar"));
        Assert.Contains("Operations", cut.Markup);
    }

    [Fact]
    public void Renders_EmptyState_WhenNoDashboards()
    {
        SetupDashboards([]);
        var cut = Render<Dashboards>();
        cut.WaitForState(() => !cut.Markup.Contains("rz-progressbar-circular"));
        var loading = (bool)typeof(Dashboards).GetField("_loading", Priv)!.GetValue(cut.Instance)!;
        Assert.False(loading);
    }

    [Fact]
    public void EditDashboard_NavigatesTo_DashboardEdit()
    {
        SetupDashboards();
        var cut = Render<Dashboards>();
        cut.WaitForState(() => !cut.Markup.Contains("rz-progressbar-circular"));

        var dashboard = new DashboardDto { Id = 42, Name = "Test", Widgets = [] };
        var method = typeof(Dashboards).GetMethod("EditDashboard", Priv)!;
        method.Invoke(cut.Instance, [dashboard]);

        // EditDashboard navigates to /dashboards/{id}.
        var nav = Services.GetRequiredService<Bunit.TestDoubles.BunitNavigationManager>();
        Assert.EndsWith("/dashboards/42", nav.Uri);
    }

    [Fact]
    public async Task CreateDashboard_EmptyName_DoesNotProceed()
    {
        SetupDashboards([]);
        var cut = Render<Dashboards>();
        cut.WaitForState(() => !cut.Markup.Contains("rz-progressbar-circular"));

        // CreateDashboard awaits Dialog.OpenAsync<DashboardNameDialog>; close it with null → guard fires.
        var dialog = Spy();
        var method = typeof(Dashboards).GetMethod("CreateDashboard", Priv)!;
        var task = cut.InvokeAsync(() => (Task)method.Invoke(cut.Instance, [])!);
        await cut.InvokeAsync(() => dialog.Close(null));
        await task;

        Assert.Equal(1, dialog.OpenCount); // the SUT actually opened a dialog (not just the test closing one)
        Assert.True(dialog.Closed);
        // Count didn't change from 0
        var dashboards = (List<DashboardDto>)typeof(Dashboards).GetField("_dashboards", Priv)!.GetValue(cut.Instance)!;
        Assert.Empty(dashboards);
    }

    [Fact]
    public void Loading_FalseAfterInit()
    {
        SetupDashboards();
        var cut = Render<Dashboards>();
        cut.WaitForState(() => !cut.Markup.Contains("rz-progressbar-circular"));
        var loading = (bool)typeof(Dashboards).GetField("_loading", Priv)!.GetValue(cut.Instance)!;
        Assert.False(loading);
    }

    [Fact]
    public void Handles_HttpError_Gracefully()
    {
        _handler.SetResponse("api/dashboards", System.Net.HttpStatusCode.InternalServerError);
        var cut = Render<Dashboards>();
        cut.WaitForState(() => !cut.Markup.Contains("rz-progressbar-circular"));
        var dashboards = (List<DashboardDto>)typeof(Dashboards).GetField("_dashboards", Priv)!.GetValue(cut.Instance)!;
        Assert.Empty(dashboards);
    }

    [Fact]
    public void NonAdmin_DoesNotLoadDashboards()
    {
        var ctx2 = new BunitContext();
        var h2 = BunitTestHelper.RegisterServices(ctx2, isAdmin: false);
        h2.SetJsonResponse("api/dashboards", new List<DashboardDto>());

        var cut = ctx2.Render<Dashboards>();

        // The non-admin guard redirects to "/" and never fetches the dashboard list.
        var nav = ctx2.Services.GetRequiredService<Bunit.TestDoubles.BunitNavigationManager>();
        Assert.EndsWith("/", nav.Uri);
        Assert.DoesNotContain(h2.Requests, r => r.Url.Contains("api/dashboards"));
    }
}
