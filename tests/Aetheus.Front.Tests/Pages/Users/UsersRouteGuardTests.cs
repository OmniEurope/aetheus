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

public class UsersRouteGuardTests : BunitContext
{
    public UsersRouteGuardTests()
    {
        BunitTestHelper.RegisterServices(this, authenticated: true, isAdmin: false);
    }

    [Fact]
    public void NonAdmin_RedirectsToHome()
    {
        Render<UsersPage>();

        var nav = Services.GetRequiredService<NavigationManager>();
        Assert.EndsWith("/", nav.Uri);
    }
}
