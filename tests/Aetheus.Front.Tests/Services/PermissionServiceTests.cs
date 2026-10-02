// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Tests;

public class PermissionServiceTests
{
    [Fact]
    public void InitialState_NotLoaded()
    {
        var svc = new PermissionService();
        Assert.False(svc.IsLoaded);
        Assert.False(svc.CanReadAny(ResourceType.Server));
        Assert.False(svc.CanRead(ResourceType.Server, 1));
        Assert.False(svc.CanWrite(ResourceType.Project, 1));
        Assert.False(svc.CanAdmin(ResourceType.Pipeline, 1));
    }

    [Fact]
    public void SetPermissions_SetsIsLoaded()
    {
        var svc = new PermissionService();
        svc.SetPermissions([], false);
        Assert.True(svc.IsLoaded);
    }

    [Fact]
    public void Clear_ResetsState()
    {
        var svc = new PermissionService();
        svc.SetPermissions([new EffectivePermissionDto { ResourceType = ResourceType.Server, Permission = Permission.Admin }], true);
        svc.Clear();
        Assert.False(svc.IsLoaded);
        Assert.Empty(svc.GetPermissions());
    }

    [Fact]
    public void Admin_HasAllPermissions()
    {
        var svc = new PermissionService();
        svc.SetPermissions([], true);

        Assert.True(svc.CanRead(ResourceType.Server));
        Assert.True(svc.CanWrite(ResourceType.Pipeline));
        Assert.True(svc.CanAdmin(ResourceType.Project));
    }

    [Fact]
    public void CanReadAny_GlobalAdmin_ReturnsTrueWithoutEntries()
    {
        var svc = new PermissionService();
        svc.SetPermissions([], true);

        Assert.True(svc.CanReadAny(ResourceType.Server));
    }

    [Fact]
    public void CanReadAny_SpecificResourcePermission_ReturnsTrue()
    {
        var svc = new PermissionService();
        svc.SetPermissions([
            new EffectivePermissionDto
            {
                ResourceType = ResourceType.Project,
                ResourceId = 42,
                Permission = Permission.Read
            }
        ], false);

        Assert.True(svc.CanReadAny(ResourceType.Project));
    }

    [Fact]
    public void CanReadAny_NoMatchingPermission_ReturnsFalse()
    {
        var svc = new PermissionService();
        svc.SetPermissions([
            new EffectivePermissionDto
            {
                ResourceType = ResourceType.Project,
                ResourceId = 42,
                Permission = Permission.Read
            }
        ], false);

        Assert.False(svc.CanReadAny(ResourceType.Server));
    }

    [Fact]
    public void ReadPermission_CanRead_CannotWrite()
    {
        var svc = new PermissionService();
        svc.SetPermissions([
            new EffectivePermissionDto
            {
                ResourceType = ResourceType.Server,
                Permission = Permission.Read,
                ResourceId = null
            }
        ], false);

        Assert.True(svc.CanRead(ResourceType.Server));
        Assert.False(svc.CanWrite(ResourceType.Server));
        Assert.False(svc.CanAdmin(ResourceType.Server));
    }

    [Fact]
    public void WritePermission_CanReadAndWrite_CannotAdmin()
    {
        var svc = new PermissionService();
        svc.SetPermissions([
            new EffectivePermissionDto
            {
                ResourceType = ResourceType.Pipeline,
                Permission = Permission.Write,
                ResourceId = null
            }
        ], false);

        Assert.True(svc.CanRead(ResourceType.Pipeline));
        Assert.True(svc.CanWrite(ResourceType.Pipeline));
        Assert.False(svc.CanAdmin(ResourceType.Pipeline));
    }

    [Fact]
    public void AdminPermission_CanDoEverything()
    {
        var svc = new PermissionService();
        svc.SetPermissions([
            new EffectivePermissionDto
            {
                ResourceType = ResourceType.Server,
                Permission = Permission.Admin,
                ResourceId = null
            }
        ], false);

        Assert.True(svc.CanRead(ResourceType.Server));
        Assert.True(svc.CanWrite(ResourceType.Server));
        Assert.True(svc.CanAdmin(ResourceType.Server));
    }

    [Fact]
    public void SpecificResourceId_OnlyMatchesThatResource()
    {
        var svc = new PermissionService();
        svc.SetPermissions([
            new EffectivePermissionDto
            {
                ResourceType = ResourceType.Server,
                Permission = Permission.Write,
                ResourceId = 5
            }
        ], false);

        Assert.True(svc.HasPermission(ResourceType.Server, 5, Permission.Write));
        Assert.False(svc.HasPermission(ResourceType.Server, 10, Permission.Write));
    }

    [Fact]
    public void WildcardPermission_MatchesAnyResourceId()
    {
        var svc = new PermissionService();
        svc.SetPermissions([
            new EffectivePermissionDto
            {
                ResourceType = ResourceType.Pipeline,
                Permission = Permission.Read,
                ResourceId = null
            }
        ], false);

        Assert.True(svc.HasPermission(ResourceType.Pipeline, 1, Permission.Read));
        Assert.True(svc.HasPermission(ResourceType.Pipeline, 999, Permission.Read));
    }

    [Fact]
    public void NoPermission_ForDifferentResourceType()
    {
        var svc = new PermissionService();
        svc.SetPermissions([
            new EffectivePermissionDto
            {
                ResourceType = ResourceType.Server,
                Permission = Permission.Admin,
                ResourceId = null
            }
        ], false);

        Assert.False(svc.CanRead(ResourceType.Pipeline));
    }

    [Fact]
    public void OnPermissionsChanged_FiresOnSet()
    {
        var svc = new PermissionService();
        var fired = false;
        svc.OnPermissionsChanged += () => fired = true;

        svc.SetPermissions([], false);
        Assert.True(fired);
    }

    [Fact]
    public void OnPermissionsChanged_FiresOnClear()
    {
        var svc = new PermissionService();
        svc.SetPermissions([], false);
        var fired = false;
        svc.OnPermissionsChanged += () => fired = true;

        svc.Clear();
        Assert.True(fired);
    }

    [Fact]
    public void GetPermissions_ReturnsCurrentList()
    {
        var svc = new PermissionService();
        var perms = new List<EffectivePermissionDto>
        {
            new() { ResourceType = ResourceType.Server, Permission = Permission.Read }
        };
        svc.SetPermissions(perms, false);

        Assert.Single(svc.GetPermissions());
    }

    [Fact]
    public void HasPermission_NullResourceId_WhenPermHasSpecificId_ReturnsFalse()
    {
        var svc = new PermissionService();
        svc.SetPermissions([
            new EffectivePermissionDto
            {
                ResourceType = ResourceType.Server,
                Permission = Permission.Read,
                ResourceId = 5
            }
        ], false);

        Assert.False(svc.HasPermission(ResourceType.Server, null, Permission.Read));
    }
}
