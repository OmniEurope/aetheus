// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Pages.Projects.Sections;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Aetheus.Front.Tests.Pages.Projects;

public class ExternalRepoRouteTests : BunitContext
{
    public ExternalRepoRouteTests() => BunitTestHelper.RegisterServices(this);

    [Fact]
    public void LegacyRoute_RedirectsToProjectGitSection()
    {
        Render<ExternalRepo>(parameters => parameters.Add(component => component.Id, 7));

        Assert.EndsWith("/git-repositories?projectId=7#external-repository",
            Services.GetRequiredService<NavigationManager>().Uri);
    }
}
