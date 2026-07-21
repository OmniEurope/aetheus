// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Pages.Users;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Bunit;
using Radzen;

namespace Aetheus.Front.Tests.Pages.Users;

public class UserEditDeepTests : BunitContext
{
    private static readonly BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;
    private readonly BunitTestHelper.TestHandler _handler;

    public UserEditDeepTests()
    {
        _handler = BunitTestHelper.RegisterServices(this, isAdmin: true);
    }

    private void SetupExistingUser(int id = 5)
    {
        _handler.SetJsonResponse("api/users/roles", new List<string> { "Admin", "Contributor", "Reader" });
        _handler.SetJsonResponse($"api/users/{id}", new UserDto
        {
            Id = id,
            Username = "tester",
            Email = "tester@example.com",
            IsActive = true,
            Roles = ["Contributor"]
        });
        _handler.SetJsonResponse($"api/users/{id}/effective-permissions", new UserPermissionSummaryDto
        {
            EffectivePermissions = [
                new EffectivePermissionDto { ResourceType = ResourceType.Server, Permission = Permission.Read }
            ]
        });
    }

    private void SetupNewUser()
    {
        _handler.SetJsonResponse("api/users/roles", new List<string> { "Admin", "Contributor", "Reader" });
    }

    [Fact]
    public void Renders_EditForm_ForExistingUser()
    {
        SetupExistingUser();
        var cut = Render<UserEdit>(p => p.Add(x => x.Id, 5));
        cut.WaitForState(() => !cut.Markup.Contains("rz-progressbar-circular"));

        // The existing user is fetched (GET api/users/5) and mapped into the edit model.
        Assert.Contains(_handler.Requests, r => r.Method == "GET" && r.Url.Contains("api/users/5"));
        var model = typeof(UserEdit).GetField("_model", Priv)!.GetValue(cut.Instance)!;
        var username = (string)model.GetType().GetProperty("Username")!.GetValue(model)!;
        Assert.Equal("tester", username);
    }

    [Fact]
    public void Renders_CreateForm_ForNewUser()
    {
        SetupNewUser();
        var cut = Render<UserEdit>(p => p.Add(x => x.Id, (int?)null));
        cut.WaitForState(() => !cut.Markup.Contains("rz-progressbar-circular"));

        // New-user mode loads the available roles but never fetches a user detail.
        Assert.Contains(_handler.Requests, r => r.Url.Contains("api/users/roles"));
        Assert.DoesNotContain(_handler.Requests, r => r.Url.Contains("/effective-permissions"));
    }

    [Fact]
    public async Task OnSubmit_NewUser_EmptyPassword_ShowsError()
    {
        SetupNewUser();
        var cut = Render<UserEdit>(p => p.Add(x => x.Id, (int?)null));
        cut.WaitForState(() => !cut.Markup.Contains("rz-progressbar-circular"));

        // Leave password empty → guard fires before any create call
        var method = typeof(UserEdit).GetMethod("OnSubmit", Priv)!;
        await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance, [])!);

        // The empty-password guard short-circuits: no create POST reaches api/users.
        Assert.DoesNotContain(_handler.Requests, r => r.Method == "POST" && r.Url.Contains("api/users"));
    }

    [Fact]
    public async Task OnSubmit_NewUser_ValidPassword_CreatesUser()
    {
        SetupNewUser();
        _handler.SetJsonResponse("api/users", new UserDto { Id = 10, Username = "newuser", Email = null, IsActive = true, Roles = [] });
        var cut = Render<UserEdit>(p => p.Add(x => x.Id, (int?)null));
        cut.WaitForState(() => !cut.Markup.Contains("rz-progressbar-circular"));

        // Set a valid password
        var modelField = typeof(UserEdit).GetField("_model", Priv)!;
        var model = modelField.GetValue(cut.Instance)!;
        var modelType = model.GetType();
        modelType.GetProperty("Username")!.SetValue(model, "newuser");
        modelType.GetProperty("Password")!.SetValue(model, "secure-passphrase");

        var method = typeof(UserEdit).GetMethod("OnSubmit", Priv)!;
        await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance, [])!);

        // A valid password passes the guard, so a create POST is sent to the users collection.
        Assert.Contains(_handler.Requests, r => r.Method == "POST" && r.Url.EndsWith("api/users"));
    }

    [Fact]
    public async Task OnSubmit_ExistingUser_UpdatesUser()
    {
        SetupExistingUser();
        _handler.SetJsonResponse("api/users/5", new UserDto { Id = 5, Username = "tester", Email = "tester@example.com", IsActive = true, Roles = ["Contributor"] });
        var cut = Render<UserEdit>(p => p.Add(x => x.Id, 5));
        cut.WaitForState(() => !cut.Markup.Contains("rz-progressbar-circular"));

        var method = typeof(UserEdit).GetMethod("OnSubmit", Priv)!;
        await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance, [])!);

        // Existing-user path issues an update PUT to api/users/5.
        Assert.Contains(_handler.Requests, r => r.Method == "PUT" && r.Url.Contains("api/users/5"));
    }

    [Fact]
    public async Task OnChangePassword_EmptyPassword_Returns()
    {
        SetupExistingUser();
        var cut = Render<UserEdit>(p => p.Add(x => x.Id, 5));
        cut.WaitForState(() => !cut.Markup.Contains("rz-progressbar-circular"));

        SetFormValue(cut.Instance, "_passwordChange", "NewPassword", "");
        await cut.InvokeAsync(() => InvokeFormSubmitAsync(cut.Instance, "OnChangePassword", "_passwordChange"));

        // An empty password short-circuits before the change-password call is made.
        Assert.DoesNotContain(_handler.Requests, r => r.Url.Contains("change-password"));
    }

    [Fact]
    public async Task OnChangePassword_ValidPassword_CallsApi()
    {
        SetupExistingUser();
        _handler.SetResponse("api/users/5/change-password", System.Net.HttpStatusCode.OK);
        var cut = Render<UserEdit>(p => p.Add(x => x.Id, 5));
        cut.WaitForState(() => !cut.Markup.Contains("rz-progressbar-circular"));

        SetFormValue(cut.Instance, "_passwordChange", "NewPassword", "newpassword1");
        await cut.InvokeAsync(() => InvokeFormSubmitAsync(cut.Instance, "OnChangePassword", "_passwordChange"));

        // A valid password triggers the change-password POST to api/users/5/change-password.
        Assert.Contains(_handler.Requests, r => r.Method == "POST" && r.Url.Contains("api/users/5/change-password"));
    }

    [Fact]
    public void OnRoleToggled_AddsAndRemovesRole()
    {
        SetupNewUser();
        var cut = Render<UserEdit>(p => p.Add(x => x.Id, (int?)null));
        cut.WaitForState(() => !cut.Markup.Contains("rz-progressbar-circular"));

        var method = typeof(UserEdit).GetMethod("OnRoleToggled", Priv)!;
        // Add role
        method.Invoke(cut.Instance, ["Admin", true]);
        var modelField = typeof(UserEdit).GetField("_model", Priv)!;
        var model = modelField.GetValue(cut.Instance)!;
        var roles = (List<string>)model.GetType().GetProperty("Roles")!.GetValue(model)!;
        Assert.Contains("Admin", roles);

        // Remove role
        method.Invoke(cut.Instance, ["Admin", false]);
        Assert.DoesNotContain("Admin", roles);
    }

    [Theory]
    [InlineData(Permission.Admin, BadgeStyle.Danger)]
    [InlineData(Permission.Write, BadgeStyle.Warning)]
    [InlineData(Permission.Read, BadgeStyle.Info)]
    public void GetPermissionBadgeStyle_ReturnsExpected(Permission permission, BadgeStyle expected)
    {
        SetupNewUser();
        var cut = Render<UserEdit>(p => p.Add(x => x.Id, (int?)null));
        var method = typeof(UserEdit).GetMethod("GetPermissionBadgeStyle", BindingFlags.NonPublic | BindingFlags.Static)!;
        var result = (BadgeStyle)method.Invoke(null, [permission])!;
        Assert.Equal(expected, result);
    }
}
