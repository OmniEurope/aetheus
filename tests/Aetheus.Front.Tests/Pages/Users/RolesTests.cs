// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Pages;
using Aetheus.Shared.DTOs;
using Bunit;
using Radzen;
using Radzen.Blazor;

namespace Aetheus.Front.Tests.Pages;

public class RolesTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public RolesTests()
    {
        _handler = BunitTestHelper.RegisterServices(this, isAdmin: true);
    }

    [Fact]
    public async Task GridReload_LoadsAndRendersPaginatedRoles()
    {
        _handler.SetJsonResponse("api/roles", new PaginatedResult<RoleDto>
        {
            Items =
            [
                new() { Id = 1, Name = "Admin", UserCount = 2 },
                new() { Id = 2, Name = "Viewer", UserCount = 5 }
            ],
            TotalCount = 2
        });

        var cut = Render<Roles>();
        var grid = cut.FindComponent<RadzenDataGrid<RoleDto>>();
        await cut.InvokeAsync(grid.Instance.Reload);

        cut.WaitForAssertion(() => Assert.Multiple(() =>
        {
            Assert.Contains("Admin", cut.Markup);
            Assert.Contains("Viewer", cut.Markup);
            Assert.DoesNotContain("rz-data-grid-loading", cut.Markup);
            Assert.Contains(_handler.Requests, request => request.Url.Contains("api/roles", StringComparison.Ordinal));
        }));
    }

    [Fact]
    public void Renders_RolesPage_Empty()
    {
        _handler.SetJsonResponse("api/roles", new PaginatedResult<RoleDto>());

        var cut = Render<Roles>();
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Roles", cut.Markup);
            Assert.DoesNotContain("rz-data-grid-loading", cut.Markup);
        });
    }

    [Fact]
    public void NonAdmin_RedirectsToHome()
    {
        var handler = BunitTestHelper.RegisterServices(this, isAdmin: false);
        handler.SetJsonResponse("api/roles", new PaginatedResult<RoleDto>());

        var cut = Render<Roles>();
        // The admin guard redirects before LoadAsync, so the roles list is never fetched.
        Assert.DoesNotContain(handler.Requests, r => r.Url.Contains("api/roles"));
    }
}
