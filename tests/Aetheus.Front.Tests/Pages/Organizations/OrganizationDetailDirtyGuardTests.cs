// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Pages.Organizations;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.DTOs.Organizations;
using Aetheus.Shared.Enums;
using Bunit;

namespace Aetheus.Front.Tests;

/// <summary>
/// Coverage for the "don't clobber an unsaved edit" guard on <see cref="OrganizationDetail"/>'s
/// realtime reload: a live <c>Organization</c> broadcast (any org, not just this one) used to
/// unconditionally re-hydrate <c>_general</c>/<c>_selectedProjectIds</c> and flip the loading
/// spinner, wiping whatever the user was mid-typing in the General tab or mid-selecting in the
/// Projects tab. The fix gates the reload behind <c>IsGeneralTabDirty</c>/<c>IsProjectsTabDirty</c>,
/// exercised here via reflection since the SignalR round-trip itself is not observable in a
/// standalone bUnit render (the hub never actually connects in this harness).
/// </summary>
public class OrganizationDetailDirtyGuardTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public OrganizationDetailDirtyGuardTests()
    {
        _handler = BunitTestHelper.RegisterServices(this, authenticated: true, isAdmin: true);
    }

    private static readonly OrganizationDetailDto Org = new(
        1, "Acme Corp", "acme", "A test org",
        DateTime.UtcNow, DateTime.UtcNow,
        [],
        [new OrganizationProjectDto(7, "Project 7", ProjectStatus.Active)]);

    private IRenderedComponent<OrganizationDetail> RenderLoaded()
    {
        _handler.SetJsonResponse("api/organizations/1", Org);
        _handler.SetJsonResponse("api/users", new PaginatedResult<UserDto> { Items = [], TotalCount = 0 });
        _handler.SetJsonResponse("api/projects", new PaginatedResult<ProjectDto>
        {
            Items = [new ProjectDto { Id = 7, Name = "Project 7" }],
            TotalCount = 1
        });

        return Render<OrganizationDetail>(p => p.Add(x => x.Id, 1));
    }

    private static bool InvokeDirtyCheck(OrganizationDetail instance, string methodName)
    {
        var method = typeof(OrganizationDetail).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (bool)method.Invoke(instance, [])!;
    }

    private static void SetGeneralName(OrganizationDetail instance, string name)
    {
        var generalField = typeof(OrganizationDetail).GetField("_general", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var general = generalField.GetValue(instance)!;
        general.GetType().GetProperty("Name")!.SetValue(general, name);
    }

    [Fact]
    public void IsGeneralTabDirty_FalseImmediatelyAfterLoad()
    {
        var cut = RenderLoaded();

        Assert.False(InvokeDirtyCheck(cut.Instance, "IsGeneralTabDirty"));
    }

    [Fact]
    public void IsGeneralTabDirty_TrueAfterEditingName()
    {
        var cut = RenderLoaded();

        SetGeneralName(cut.Instance, "Renamed while a broadcast could arrive");

        Assert.True(InvokeDirtyCheck(cut.Instance, "IsGeneralTabDirty"));
    }

    [Fact]
    public void IsProjectsTabDirty_FalseImmediatelyAfterLoad()
    {
        var cut = RenderLoaded();

        Assert.False(InvokeDirtyCheck(cut.Instance, "IsProjectsTabDirty"));
    }

    [Fact]
    public void IsProjectsTabDirty_TrueAfterChangingSelection()
    {
        var cut = RenderLoaded();

        var field = typeof(OrganizationDetail).GetField("_selectedProjectIds", BindingFlags.NonPublic | BindingFlags.Instance)!;
        field.SetValue(cut.Instance, new List<int> { 7, 42 });

        Assert.True(InvokeDirtyCheck(cut.Instance, "IsProjectsTabDirty"));
    }
}
