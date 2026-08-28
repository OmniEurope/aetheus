// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Layout;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Aetheus.Front.Tests;

public class BreadcrumbServiceTests : BunitContext
{
    private NavigationManager Nav => Services.GetRequiredService<NavigationManager>();

    [Fact]
    public void Items_InitiallyEmpty()
    {
        var sut = new BreadcrumbService(Nav);
        Assert.Empty(sut.Items);
    }

    [Fact]
    public void Set_AddsItemsAndFiresOnChanged()
    {
        var sut = new BreadcrumbService(Nav);
        var changed = false;
        sut.OnChanged += () => changed = true;

        sut.Set(new BreadcrumbItem("Home", "/"), new BreadcrumbItem("Servers", "/servers"));

        Assert.Equal(2, sut.Items.Count);
        Assert.Equal("Home", sut.Items[0].Text);
        Assert.Equal("/servers", sut.Items[1].Href);
        Assert.True(changed);
    }

    [Fact]
    public void Clear_RemovesItemsAndFiresOnChanged()
    {
        var sut = new BreadcrumbService(Nav);
        sut.Set(new BreadcrumbItem("Home", "/"));

        var changed = false;
        sut.OnChanged += () => changed = true;

        sut.Clear();

        Assert.Empty(sut.Items);
        Assert.True(changed);
    }

    [Fact]
    public void Set_ReplacesExistingItems()
    {
        var sut = new BreadcrumbService(Nav);
        sut.Set(new BreadcrumbItem("Old"));

        sut.Set(new BreadcrumbItem("New"));

        Assert.Single(sut.Items);
        Assert.Equal("New", sut.Items[0].Text);
    }

    [Fact]
    public void ParentHref_ReturnsNearestClickableAncestor()
    {
        var sut = new BreadcrumbService(Nav);
        sut.Set(
            new BreadcrumbItem("Projects", "/projects"),
            new BreadcrumbItem("Project without a link"),
            new BreadcrumbItem("Pipelines", "/pipelines?projectId=42"),
            new BreadcrumbItem("Current pipeline"));

        Assert.Equal("/pipelines?projectId=42", sut.ParentHref);
    }

    [Fact]
    public void ParentHref_DoesNotUseCurrentItemHref()
    {
        var sut = new BreadcrumbService(Nav);
        sut.Set(new BreadcrumbItem("Current page", "/current-page"));

        Assert.Null(sut.ParentHref);
    }

    [Fact]
    public void LocationChanged_ClearsItemsAndFiresOnChanged()
    {
        var sut = new BreadcrumbService(Nav);
        sut.Set(new BreadcrumbItem("Home", "/"));

        var changed = false;
        sut.OnChanged += () => changed = true;

        Nav.NavigateTo("/other-page");

        Assert.Empty(sut.Items);
        Assert.True(changed);
    }

    [Fact]
    public void LocationChanged_WithFallback_ReplacesItemsWithoutAnEmptyState()
    {
        var sut = new BreadcrumbService(Nav);
        sut.ConfigureFallback(path => [new BreadcrumbItem($"fallback:{path}")]);
        sut.Set(new BreadcrumbItem("Loaded page"));

        Nav.NavigateTo("/other-page");

        var item = Assert.Single(sut.Items);
        Assert.Contains("other-page", item.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void LocationChanged_QueryOnly_KeepsItems()
    {
        // A tab switch via UrlSyncedTabs changes only the ?tab= query on the SAME path. The breadcrumb,
        // set once by the owning page behind an early-return guard, must survive that navigation - the
        // regression the user reported ("le fil d'Ariane est perdu dès qu'on clique sur un onglet").
        var sut = new BreadcrumbService(Nav);
        Nav.NavigateTo("/users/5");
        sut.Set(new BreadcrumbItem("Users", "/users"), new BreadcrumbItem("alice"));

        var changed = false;
        sut.OnChanged += () => changed = true;

        Nav.NavigateTo("/users/5?tab=roles");

        Assert.Equal(2, sut.Items.Count);
        Assert.False(changed);
    }

    [Fact]
    public void LocationChanged_TrailingSlashOnly_KeepsItems()
    {
        // A stray trailing slash (from a link/redirect) is the same logical page - it must not read as a
        // page change and wipe the breadcrumb.
        var sut = new BreadcrumbService(Nav);
        Nav.NavigateTo("/settings");
        sut.Set(new BreadcrumbItem("Settings"));

        Nav.NavigateTo("/settings/");

        Assert.Single(sut.Items);
    }

    [Fact]
    public void LocationChanged_PathChangeFromTab_ClearsItems()
    {
        // Leaving the page entirely (path changes, not just the query) must still reset so the next
        // page starts clean.
        var sut = new BreadcrumbService(Nav);
        Nav.NavigateTo("/users/5?tab=roles");
        sut.Set(new BreadcrumbItem("Users", "/users"), new BreadcrumbItem("alice"));

        Nav.NavigateTo("/servers");

        Assert.Empty(sut.Items);
    }

    [Fact]
    public void Dispose_UnsubscribesFromLocationChanged()
    {
        var sut = new BreadcrumbService(Nav);
        sut.Set(new BreadcrumbItem("Home", "/"));

        sut.Dispose();

        var changed = false;
        sut.OnChanged += () => changed = true;
        Nav.NavigateTo("/after-dispose");

        Assert.Single(sut.Items);
        Assert.False(changed);
    }

    [Fact]
    public void BreadcrumbItem_RecordEquality()
    {
        var a = new BreadcrumbItem("Home", "/");
        var b = new BreadcrumbItem("Home", "/");
        Assert.Equal(a, b);
    }
}
