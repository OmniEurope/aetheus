// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Shared;
using Aetheus.Shared.DTOs;
using Bunit;
using Radzen;
using Radzen.Blazor;

namespace Aetheus.Front.Tests.Shared;

public class RoleAssignmentPickerTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public RoleAssignmentPickerTests()
    {
        _handler = BunitTestHelper.RegisterServices(this, authenticated: true, isAdmin: true);
    }

    private void SetupRoles(params RoleDto[] roles) =>
        _handler.SetJsonResponse("api/roles", new PaginatedResult<RoleDto>
        {
            Items = roles.ToList(),
            TotalCount = 4280,
            Page = 1,
            PageSize = 25
        });

    [Fact]
    public void InitialLoad_UsesPaginatedRolesEndpoint()
    {
        SetupRoles(new RoleDto
        {
            Id = 1,
            Name = "Admin",
            Description = "Full access",
            PermissionCount = 11,
            UserCount = 1
        });

        var cut = Render<RoleAssignmentPicker>(parameters =>
            parameters.Add(component => component.SelectedRoles, []));

        cut.WaitForAssertion(() => Assert.Contains("Admin", cut.Markup));

        Assert.Contains(_handler.Requests, request =>
            request.Url.Contains("api/roles?page=1&pageSize=25", StringComparison.Ordinal));
        Assert.DoesNotContain(_handler.Requests, request =>
            request.Url.Contains("api/users/roles", StringComparison.Ordinal));
        Assert.NotNull(cut.FindComponent<RadzenTextBox>());
    }

    [Fact]
    public void Search_ReloadsFirstPageWithServerQuery()
    {
        SetupRoles(new RoleDto { Id = 2, Name = "Security Auditor" });
        var cut = Render<RoleAssignmentPicker>(parameters =>
            parameters.Add(component => component.SelectedRoles, []));
        cut.WaitForAssertion(() => Assert.Contains("Security Auditor", cut.Markup));
        _handler.Requests.Clear();

        cut.Find(".role-assignment-search").Input("security");

        cut.WaitForAssertion(() => Assert.Contains(_handler.Requests, request =>
            request.Url.Contains("search=security", StringComparison.OrdinalIgnoreCase)));

        cut.Find(".role-assignment-toolbar button").Click();

        cut.WaitForAssertion(() =>
            Assert.True(string.IsNullOrEmpty(
                cut.Find(".role-assignment-search").GetAttribute("value"))));
        Assert.Contains(_handler.Requests, request =>
            request.Url.Contains("api/roles?page=1&pageSize=25", StringComparison.Ordinal)
            && !request.Url.Contains("search=", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AdvancedNameFilter_UsesThePagedServerSearch()
    {
        SetupRoles(new RoleDto { Id = 3, Name = "Security Auditor" });
        var cut = Render<RoleAssignmentPicker>(parameters =>
            parameters.Add(component => component.SelectedRoles, []));
        cut.WaitForAssertion(() => Assert.Contains("Security Auditor", cut.Markup));
        _handler.Requests.Clear();
        var load = typeof(RoleAssignmentPicker).GetMethod(
            "LoadDataAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var args = new LoadDataArgs
        {
            Skip = 0,
            Top = 25,
            Filters =
            [
                new FilterDescriptor
                {
                    Property = nameof(RoleDto.Name),
                    FilterValue = "auditor",
                    FilterOperator = FilterOperator.Contains
                }
            ]
        };

        await cut.InvokeAsync(async () => await (Task)load.Invoke(cut.Instance, [args])!);

        Assert.Contains(_handler.Requests, request =>
            request.Url.Contains("search=auditor", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AdvancedNameFilter_ThroughRenderedControls_UsesServerSearch()
    {
        SetupRoles(new RoleDto { Id = 3, Name = "Security Auditor" });
        var cut = Render<RoleAssignmentPicker>(parameters =>
            parameters.Add(component => component.SelectedRoles, []));
        cut.WaitForAssertion(() => Assert.Contains("Security Auditor", cut.Markup));
        _handler.Requests.Clear();

        cut.Find(".role-assignment-grid .rz-grid-filter-icon").MouseDown();
        cut.Find(".role-assignment-grid .rz-grid-filter input.rz-textbox").Change("auditor");
        cut.Find(".role-assignment-grid form.rz-grid-filter").Submit();

        cut.WaitForAssertion(() => Assert.Contains(_handler.Requests, request =>
            request.Url.Contains("search=auditor", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void Toggle_PreservesSelectionOutsideTheLoadedPage()
    {
        SetupRoles(new RoleDto { Id = 2, Name = "Security Auditor" });
        List<string>? changed = null;
        var cut = Render<RoleAssignmentPicker>(parameters => parameters
            .Add(component => component.SelectedRoles, ["Existing"])
            .Add(component => component.SelectedRolesChanged,
                roles => changed = roles));
        cut.WaitForAssertion(() => Assert.Contains("Security Auditor", cut.Markup));

        cut.Find(".role-assignment-toggle input").Change(true);

        Assert.NotNull(changed);
        Assert.Contains("Existing", changed);
        Assert.Contains("Security Auditor", changed);
    }

    [Fact]
    public void AssignedOnly_ShowsOnlyTheCurrentSelection()
    {
        SetupRoles(
            new RoleDto { Id = 1, Name = "Admin" },
            new RoleDto { Id = 2, Name = "Reader" });
        var cut = Render<RoleAssignmentPicker>(parameters =>
            parameters.Add(component => component.SelectedRoles, ["Admin"]));
        cut.WaitForAssertion(() =>
            Assert.Equal(2, cut.FindAll(".role-assignment-grid tbody tr").Count));

        cut.Find(".role-assignment-filter input").Change(true);

        cut.WaitForAssertion(() =>
        {
            var rows = cut.FindAll(".role-assignment-grid tbody tr");
            Assert.Single(rows);
            Assert.Contains("Admin", rows[0].TextContent);
            Assert.DoesNotContain("Reader", rows[0].TextContent);
        });
    }
}
