// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Pages;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Radzen.Blazor;

namespace Aetheus.Front.Tests.Pages;

public class UserEditTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public UserEditTests()
    {
        _handler = BunitTestHelper.RegisterServices(this, isAdmin: true);
    }

    private void SetupMocks(int id = 1)
    {
        _handler.SetJsonResponse("api/users/roles", new List<string> { "Admin", "Viewer", "Editor" });
        _handler.SetJsonResponse($"api/users/{id}", new UserDto
        {
            Id = id,
            Username = "testuser",
            Email = "test@example.com",
            IsActive = true,
            Roles = ["Viewer"]
        });
        _handler.SetJsonResponse($"api/users/{id}/effective-permissions", new UserPermissionSummaryDto
        {
            EffectivePermissions =
            [
                new EffectivePermissionDto { ResourceType = ResourceType.Server, Permission = Permission.Read }
            ]
        });
        _handler.SetJsonResponse("api/users", new UserDto { Id = id, Username = "testuser" });
    }

    [Fact]
    public void NewUser_RendersEmptyForm()
    {
        _handler.SetJsonResponse("api/users/roles", new List<string> { "Admin", "Viewer" });
        var cut = Render<UserEdit>(p => p.Add(x => x.Id, null));
        cut.WaitForState(() => !cut.Markup.Contains("rz-progressbar-circular"), TimeSpan.FromSeconds(2));

        // New-user mode fetches the available roles but never a user detail.
        Assert.Contains(_handler.Requests, r => r.Url.Contains("api/users/roles"));
        Assert.DoesNotContain(_handler.Requests, r => r.Url.Contains("/effective-permissions"));
    }

    [Fact]
    public void EditUser_LoadsExistingData()
    {
        SetupMocks();
        var cut = Render<UserEdit>(p => p.Add(x => x.Id, 1));
        cut.WaitForState(() => !cut.Markup.Contains("rz-progressbar-circular"), TimeSpan.FromSeconds(2));

        // The existing user is fetched and its username is mapped into the form model.
        Assert.Contains(_handler.Requests, r => r.Method == "GET" && r.Url.Contains("api/users/1"));
        var model = typeof(UserEdit).GetField("_model", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance)!;
        Assert.Equal("testuser", (string)model.GetType().GetProperty("Username")!.GetValue(model)!);
    }

    [Fact]
    public void NonAdmin_RedirectsToHome()
    {
        var handler = BunitTestHelper.RegisterServices(this, isAdmin: false);
        handler.SetJsonResponse("api/users/roles", new List<string>());
        Render<UserEdit>(p => p.Add(x => x.Id, 1));

        // The non-admin guard returns before any roles/user data is fetched.
        Assert.DoesNotContain(handler.Requests, r => r.Url.Contains("api/users/roles"));
    }

    [Fact]
    public void EditUser_WithMultipleRoles_LoadsCorrectly()
    {
        _handler.SetJsonResponse("api/users/roles", new List<string> { "Admin", "Viewer", "Editor" });
        _handler.SetJsonResponse("api/users/2", new UserDto
        {
            Id = 2,
            Username = "admin-user",
            Email = "admin@example.com",
            IsActive = true,
            Roles = ["Admin", "Editor"]
        });
        _handler.SetJsonResponse("api/users/2/permissions", new UserPermissionSummaryDto
        {
            EffectivePermissions = []
        });

        var cut = Render<UserEdit>(p => p.Add(x => x.Id, 2));
        cut.WaitForState(() => !cut.Markup.Contains("rz-progressbar-circular"), TimeSpan.FromSeconds(2));

        // Both assigned roles are loaded into the form model.
        var model = typeof(UserEdit).GetField("_model", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance)!;
        var roles = (List<string>)model.GetType().GetProperty("Roles")!.GetValue(model)!;
        Assert.Contains("Admin", roles);
        Assert.Contains("Editor", roles);
    }

    [Fact]
    public void EditUser_InactiveUser_LoadsCorrectly()
    {
        _handler.SetJsonResponse("api/users/roles", new List<string> { "Viewer" });
        _handler.SetJsonResponse("api/users/3", new UserDto
        {
            Id = 3,
            Username = "inactive-user",
            Email = null,
            IsActive = false,
            Roles = []
        });
        _handler.SetJsonResponse("api/users/3/permissions", new UserPermissionSummaryDto
        {
            EffectivePermissions = []
        });

        var cut = Render<UserEdit>(p => p.Add(x => x.Id, 3));
        cut.WaitForState(() => !cut.Markup.Contains("rz-progressbar-circular"), TimeSpan.FromSeconds(2));

        // The inactive flag is carried into the form model.
        var model = typeof(UserEdit).GetField("_model", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance)!;
        Assert.False((bool)model.GetType().GetProperty("IsActive")!.GetValue(model)!);
    }

    // --- OnRoleToggled ---

    [Fact]
    public void OnRoleToggled_AddRole()
    {
        SetupMocks();
        var cut = Render<UserEdit>(p => p.Add(x => x.Id, 1));

        var method = typeof(UserEdit).GetMethod("OnRoleToggled", BindingFlags.NonPublic | BindingFlags.Instance)!;
        method.Invoke(cut.Instance, ["Admin", true]);

        var model = typeof(UserEdit).GetField("_model", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance)!;
        var roles = (List<string>)model.GetType().GetProperty("Roles")!.GetValue(model)!;
        Assert.Contains("Admin", roles);
    }

    [Fact]
    public void OnRoleToggled_RemoveRole()
    {
        SetupMocks();
        var cut = Render<UserEdit>(p => p.Add(x => x.Id, 1));

        var method = typeof(UserEdit).GetMethod("OnRoleToggled", BindingFlags.NonPublic | BindingFlags.Instance)!;
        method.Invoke(cut.Instance, ["Viewer", false]);

        var model = typeof(UserEdit).GetField("_model", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance)!;
        var roles = (List<string>)model.GetType().GetProperty("Roles")!.GetValue(model)!;
        Assert.DoesNotContain("Viewer", roles);
    }

    [Fact]
    public void OnRoleToggled_AddDuplicateRole_NoDuplicate()
    {
        SetupMocks();
        var cut = Render<UserEdit>(p => p.Add(x => x.Id, 1));

        var method = typeof(UserEdit).GetMethod("OnRoleToggled", BindingFlags.NonPublic | BindingFlags.Instance)!;
        method.Invoke(cut.Instance, ["Viewer", true]); // already in roles
        method.Invoke(cut.Instance, ["Viewer", true]); // again

        var model = typeof(UserEdit).GetField("_model", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance)!;
        var roles = (List<string>)model.GetType().GetProperty("Roles")!.GetValue(model)!;
        Assert.Single(roles, r => r == "Viewer");
    }

    // --- OnSubmit ---

    [Fact]
    public async Task OnSubmit_NewUser_ShortPassword_ShowsError()
    {
        _handler.SetJsonResponse("api/users/roles", new List<string> { "Viewer" });
        var cut = Render<UserEdit>(p => p.Add(x => x.Id, null));

        var model = typeof(UserEdit).GetField("_model", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance)!;
        model.GetType().GetProperty("Password")!.SetValue(model, "ab");

        var method = typeof(UserEdit).GetMethod("OnSubmit", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)method.Invoke(cut.Instance, [])!;

        // A < 6 char password fails the length guard: the create POST is never sent.
        Assert.DoesNotContain(_handler.Requests, r => r.Method == "POST" && r.Url.Contains("api/users"));
    }

    [Fact]
    public async Task OnSubmit_ExistingUser_UpdatesUser()
    {
        SetupMocks();

        var cut = Render<UserEdit>(p => p.Add(x => x.Id, 1));

        var method = typeof(UserEdit).GetMethod("OnSubmit", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)method.Invoke(cut.Instance, [])!;

        // The edit path issues a PUT to api/users/1 (the update the test name promises).
        Assert.Contains(_handler.Requests, r => r.Method == "PUT" && r.Url.Contains("api/users/1"));
    }

    [Fact]
    public void OrganizationsDeepLink_LoadsEveryServerPage()
    {
        SetupMocks();
        _handler.SetJsonResponse("api/users/1/organizations", new List<Aetheus.Shared.DTOs.Organizations.UserOrganizationDto>());
        _handler.SetJsonResponse("api/organizations?page=1&pageSize=200", new PaginatedResult<Aetheus.Shared.DTOs.Organizations.OrganizationDto>
        {
            Items = [new Aetheus.Shared.DTOs.Organizations.OrganizationDto(10, "First", "first", "", 0, 0, default, default)],
            TotalCount = 201,
            Page = 1,
            PageSize = 200
        });
        _handler.SetJsonResponse("api/organizations?page=2&pageSize=200", new PaginatedResult<Aetheus.Shared.DTOs.Organizations.OrganizationDto>
        {
            Items = [new Aetheus.Shared.DTOs.Organizations.OrganizationDto(11, "Last", "last", "", 0, 0, default, default)],
            TotalCount = 201,
            Page = 2,
            PageSize = 200
        });
        Services.GetRequiredService<NavigationManager>().NavigateTo("/users/1?tab=organizations");

        var cut = Render<UserEdit>(parameters => parameters.Add(component => component.Id, 1));

        cut.WaitForAssertion(() => Assert.Contains(_handler.Requests, request =>
            request.Url.Contains("api/organizations?page=2&pageSize=200", StringComparison.Ordinal)));
        var organizations = (System.Collections.IList)typeof(UserEdit)
            .GetField("_availableOrgs", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance)!;
        Assert.Equal(2, organizations.Count);
    }

    [Fact]
    public async Task OnDelete_FailedStatusDoesNotNavigate()
    {
        SetupMocks();
        _handler.SetResponse(HttpMethod.Delete, "api/users/1", System.Net.HttpStatusCode.Conflict);
        var cut = Render<UserEdit>(parameters => parameters.Add(component => component.Id, 1));
        cut.WaitForState(() => !cut.Markup.Contains("rz-progressbar-circular"), TimeSpan.FromSeconds(2));
        var nav = Services.GetRequiredService<Bunit.TestDoubles.BunitNavigationManager>();
        var dialog = Services.GetRequiredService<DialogService>();
        var method = typeof(UserEdit).GetMethod("OnDelete", BindingFlags.NonPublic | BindingFlags.Instance)!;

        var task = cut.InvokeAsync(() => (Task)method.Invoke(cut.Instance, [])!);
        await cut.InvokeAsync(() => dialog.Close(true));
        await task;

        Assert.Contains(_handler.Requests, request =>
            request.Method == "DELETE" && request.Url.EndsWith("api/users/1", StringComparison.Ordinal));
        Assert.False(nav.Uri.EndsWith("/users", StringComparison.Ordinal));
        Assert.Contains(Services.GetRequiredService<NotificationService>().Messages,
            message => message.Severity == NotificationSeverity.Error);
    }

    // --- OnChangePassword ---

    [Fact]
    public async Task OnChangePassword_ShortPassword_DoesNothing()
    {
        SetupMocks();
        var cut = Render<UserEdit>(p => p.Add(x => x.Id, 1));

        SetFormValue(cut.Instance, "_passwordChange", "NewPassword", "ab");
        await InvokeFormSubmitAsync(cut.Instance, "OnChangePassword", "_passwordChange");

        // A < 6 char password fails the length guard - no change-password call is sent.
        Assert.DoesNotContain(_handler.Requests, r => r.Url.Contains("change-password"));
    }

    [Fact]
    public async Task SecurityPassword_NativeInput_UpdatesRuleFeedbackImmediately()
    {
        SetupMocks();
        Services.GetRequiredService<NavigationManager>().NavigateTo("/users/1?tab=security");
        var cut = Render<UserEdit>(p => p.Add(x => x.Id, 1));
        cut.WaitForState(() => !cut.Markup.Contains("rz-progressbar-circular"), TimeSpan.FromSeconds(2));
        cut.WaitForState(() => cut.FindAll("input[type='password']").Count == 1);

        var password = cut.Find("input[type='password']");
        password.Input("correct-horse-battery");

        cut.WaitForAssertion(() =>
            Assert.Contains("rz-color-success", cut.Find(".rz-color-success").ClassList));
        Assert.Equal("correct-horse-battery",
            GetFormValue<string>(cut.Instance, "_passwordChange", "NewPassword"));
    }

    // --- GetPermissionBadgeStyle ---

    [Theory]
    [InlineData(Permission.Admin, BadgeStyle.Danger)]
    [InlineData(Permission.Write, BadgeStyle.Warning)]
    [InlineData(Permission.Read, BadgeStyle.Info)]
    public void GetPermissionBadgeStyle_ReturnsExpected(Permission perm, BadgeStyle expected)
    {
        var method = typeof(UserEdit).GetMethod("GetPermissionBadgeStyle", BindingFlags.NonPublic | BindingFlags.Static)!;
        var result = (BadgeStyle)method.Invoke(null, [perm])!;
        Assert.Equal(expected, result);
    }

    // --- User with permissions ---

    [Fact]
    public void EditUser_WithEffectivePermissions_ShowsPermissionTable()
    {
        _handler.SetJsonResponse("api/users/roles", new List<string> { "Admin", "Viewer" });
        _handler.SetJsonResponse("api/users/4", new UserDto
        {
            Id = 4,
            Username = "permuser",
            IsActive = true,
            Roles = ["Admin"]
        });
        _handler.SetJsonResponse("api/users/4/effective-permissions", new UserPermissionSummaryDto
        {
            EffectivePermissions =
            [
                new EffectivePermissionDto { ResourceType = ResourceType.Server, Permission = Permission.Admin },
                new EffectivePermissionDto { ResourceType = ResourceType.Pipeline, Permission = Permission.Write },
                new EffectivePermissionDto { ResourceType = ResourceType.Project, Permission = Permission.Read }
            ]
        });

        var cut = Render<UserEdit>(p => p.Add(x => x.Id, 4));
        cut.WaitForState(() =>
            typeof(UserEdit).GetField("_effectivePermissions", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance) is not null,
            TimeSpan.FromSeconds(2));

        // All three stubbed effective permissions are loaded for the permission table.
        var perms = (List<EffectivePermissionDto>?)typeof(UserEdit)
            .GetField("_effectivePermissions", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance);
        Assert.NotNull(perms);
        Assert.Equal(3, perms!.Count);
    }
}
