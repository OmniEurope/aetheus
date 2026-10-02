// SPDX-License-Identifier: EUPL-1.2
using System.Security.Claims;
using Aetheus.Back.Components.Git;
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Hubs;
using Aetheus.Back.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using NSubstitute;

namespace Aetheus.Back.Tests;

public sealed class SensitiveHubAuthorizationTests
{
    [Fact]
    public void AdminHub_RequiresAdminRoleAtNegotiation()
    {
        var authorize = Assert.Single(typeof(AdminHub).GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true).Cast<AuthorizeAttribute>());
        Assert.Equal("Admin", authorize.Roles);
    }

    [Fact]
    public async Task GitHub_CannotJoinRepositoryFromAnotherTenant()
    {
        var authz = Substitute.For<IResourceAuthorizationService>();
        var repositories = Substitute.For<IGitLightRepository>();
        repositories.FindByIdAsync(42, Arg.Any<CancellationToken>())
            .Returns(new GitInternalRepo { Id = 42, ProjectId = 8, Name = "private", Slug = "private" });
        authz.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), ResourceType.Project, 8, Permission.Read, Arg.Any<CancellationToken>())
            .Returns(false);
        var context = Substitute.For<HubCallerContext>();
        context.ConnectionId.Returns("tenant-a");
        context.User.Returns(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "alice")], "test")));
        var groups = Substitute.For<IGroupManager>();
        var hub = new GitRealtimeHub(authz, repositories);
        typeof(Hub).GetProperty("Context")!.SetValue(hub, context);
        typeof(Hub).GetProperty("Groups")!.SetValue(hub, groups);

        await Assert.ThrowsAsync<HubException>(() => hub.JoinRepositoryGroup(42));

        await groups.DidNotReceive().AddToGroupAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        Assert.Null(typeof(GitRealtimeGroups).GetField("Global"));
    }
}
