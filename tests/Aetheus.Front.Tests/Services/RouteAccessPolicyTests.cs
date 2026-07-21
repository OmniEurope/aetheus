// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;

namespace Aetheus.Front.Tests.Services;

public class RouteAccessPolicyTests
{
    [Fact]
    public void ProtectedRoute_IsDeniedBeforePermissionsLoad()
    {
        Assert.False(RouteAccessPolicy.CanAccess("servers", true, false, new PermissionService()));
    }

    [Theory]
    [InlineData("servers")]
    [InlineData("projects/42/overview")]
    [InlineData("pipelines/runs/8?tab=logs")]
    [InlineData("backups")]
    [InlineData("admin/users")]
    [InlineData("unknown-future-page")]
    public void UserWithoutRights_IsDeniedProtectedAndUnknownRoutes(string route)
    {
        var permissions = Loaded();

        Assert.False(RouteAccessPolicy.CanAccess(route, true, false, permissions));
    }

    [Theory]
    [InlineData("")]
    [InlineData("settings/tokens")]
    [InlineData("help/rbac")]
    [InlineData("account/change-password")]
    [InlineData("not-found")]
    public void AuthenticatedUser_CanAccessPersonalRoutes(string route)
    {
        Assert.True(RouteAccessPolicy.CanAccess(route, true, false, Loaded()));
    }

    [Theory]
    [InlineData("servers/7/overview", ResourceType.Server, 7)]
    [InlineData("projects/12/overview", ResourceType.Project, 12)]
    [InlineData("pipelines/5", ResourceType.Pipeline, 5)]
    [InlineData("releases/3", ResourceType.Release, 3)]
    public void SpecificReadPermission_OnlyOpensMatchingResource(
        string route,
        ResourceType resourceType,
        int resourceId)
    {
        var permissions = Loaded(new EffectivePermissionDto
        {
            ResourceType = resourceType,
            ResourceId = resourceId,
            Permission = Permission.Read
        });

        Assert.True(RouteAccessPolicy.CanAccess(route, true, false, permissions));
        Assert.False(RouteAccessPolicy.CanAccess(route.Replace(resourceId.ToString(), "999"), true, false, permissions));
    }

    [Theory]
    [InlineData("projects/12/pipelines", ResourceType.Pipeline)]
    [InlineData("projects/12/servers", ResourceType.Server)]
    [InlineData("projects/12/releases", ResourceType.Release)]
    [InlineData("projects/12/libraries", ResourceType.VariableLibrary)]
    [InlineData("projects/12/vaults", ResourceType.Vault)]
    [InlineData("projects/12/environments", ResourceType.Environment)]
    public void ProjectSubresource_UsesItsOwnResourcePermission(string route, ResourceType resourceType)
    {
        var permissions = Loaded(
            new EffectivePermissionDto
            {
                ResourceType = ResourceType.Project,
                ResourceId = 12,
                Permission = Permission.Read
            },
            new EffectivePermissionDto
            {
                ResourceType = resourceType,
                Permission = Permission.Read
            });

        Assert.True(RouteAccessPolicy.CanAccess(route, true, false, permissions));
    }

    [Fact]
    public void ReadPermission_DoesNotOpenCreateRoute()
    {
        var permissions = Loaded(new EffectivePermissionDto
        {
            ResourceType = ResourceType.Project,
            Permission = Permission.Read
        });

        Assert.False(RouteAccessPolicy.CanAccess("projects/new", true, false, permissions));
    }

    [Fact]
    public void Admin_CanAccessAdminRouteEvenIfPermissionBootstrapFailed()
    {
        Assert.True(RouteAccessPolicy.CanAccess("admin/users", true, true, new PermissionService()));
    }

    [Fact]
    public void AnonymousUser_CanOnlyAccessLogin()
    {
        var permissions = new PermissionService();

        Assert.True(RouteAccessPolicy.CanAccess("login", false, false, permissions));
        Assert.False(RouteAccessPolicy.CanAccess("settings", false, false, permissions));
    }

    private static PermissionService Loaded(params EffectivePermissionDto[] grants)
    {
        var permissions = new PermissionService();
        permissions.SetPermissions([.. grants], false);
        return permissions;
    }
}
