// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Components.Users;
using Bunit;

namespace Aetheus.Front.Tests.Pages.Users;

public class UserAndRoleCreateDialogTests : BunitContext
{
    public UserAndRoleCreateDialogTests() => BunitTestHelper.RegisterServices(this, authenticated: true, isAdmin: true);

    [Fact]
    public void UserCreateDialog_Renders_FormWithRoles()
    {
        var cut = Render<UserCreateDialog>(p => p.Add(c => c.AvailableRoles, ["Admin", "Operator"]));

        Assert.NotEmpty(cut.FindAll("input"));
    }

    [Fact]
    public void RoleCreateDialog_Renders_Form()
    {
        var cut = Render<RoleCreateDialog>();

        Assert.NotEmpty(cut.FindAll("input"));
    }
}
