// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Pages;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using UsersPage = Aetheus.Front.Pages.Users.Users;

namespace Aetheus.Front.Tests.Pages;

public class UsersTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public UsersTests()
    {
        _handler = BunitTestHelper.RegisterServices(this, authenticated: true, isAdmin: true);
    }

    [Fact]
    public void Renders_UsersList_WithData()
    {
        _handler.SetJsonResponse("api/users/roles", new List<string> { "Admin", "User" });
        _handler.SetJsonResponse("api/users", new PaginatedResult<UserDto>
        {
            Items =
            [
                new UserDto { Id = 1, Username = "admin", Email = "admin@test.com", IsActive = true, Roles = ["Admin"] },
                new UserDto { Id = 2, Username = "operator", Email = "op@test.com", IsActive = true, Roles = [] }
            ],
            TotalCount = 2,
            Page = 1,
            PageSize = 25
        });

        var cut = Render<UsersPage>();

        Assert.Contains("Users", cut.Markup);
    }

    [Fact]
    public void Renders_EmptyUsersList()
    {
        _handler.SetJsonResponse("api/users/roles", new List<string> { "Admin", "User" });
        _handler.SetJsonResponse("api/users", new PaginatedResult<UserDto>
        {
            Items = [],
            TotalCount = 0,
            Page = 1,
            PageSize = 25
        });

        var cut = Render<UsersPage>();
        Assert.Contains("Users", cut.Markup);
    }

    [Fact]
    public void Renders_UserEdit_NewUser()
    {
        _handler.SetJsonResponse("api/users/roles", new List<string> { "Admin", "User" });
        var cut = Render<UserEdit>();

        // New-user mode renders the empty create form: input fields, the available role labels, and a
        // "Create" submit button (not "Save"). It loads the roles but never fetches a user's detail.
        Assert.NotEmpty(cut.FindAll("input"));
        Assert.Contains("Admin", cut.Markup);
        Assert.Contains(cut.FindAll("button"), b => b.TextContent.Contains("Create"));
        Assert.Contains(_handler.Requests, r => r.Url.Contains("api/users/roles"));
        Assert.DoesNotContain(_handler.Requests, r => r.Url.Contains("/effective-permissions"));
    }

    [Fact]
    public void Renders_UserEdit_ExistingUser()
    {
        _handler.SetJsonResponse("api/users/roles", new List<string> { "Admin", "User" });
        _handler.SetJsonResponse("api/users/1", new UserDto
        {
            Id = 1,
            Username = "admin",
            Email = "admin@test.com",
            IsActive = true,
            Roles = ["Admin"]
        });

        var cut = Render<UserEdit>(p => p.Add(x => x.Id, 1));
        cut.WaitForState(() => cut.Markup.Contains("admin"), TimeSpan.FromSeconds(3));

        Assert.Contains("admin", cut.Markup);
    }
}
