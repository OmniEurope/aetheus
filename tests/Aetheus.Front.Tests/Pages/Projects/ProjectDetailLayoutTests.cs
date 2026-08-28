// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Layout;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Aetheus.Front.Tests.Pages.Projects;

public sealed class ProjectDetailLayoutTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public ProjectDetailLayoutTests() => _handler = BunitTestHelper.RegisterServices(this);

    [Fact]
    public async Task Loaded_Project_Name_Replaces_The_Route_Skeleton()
    {
        _handler.SetJsonResponse("api/projects/42", new ProjectDetailDto
        {
            Id = 42,
            Name = "Atlas",
            Status = ProjectStatus.Active
        });
        _handler.SetJsonResponse("api/releases", new PaginatedResult<ReleaseDto>());
        var loader = Services.GetRequiredService<ProjectDetailLoader>();
        Services.GetRequiredService<NavigationManager>().NavigateTo("/projects/42/overview");

        var cut = Render<ProjectDetailLayout>(parameters => parameters
            .Add(layout => layout.Body, builder => builder.AddMarkupContent(0, "<div/>")));
        await cut.InvokeAsync(() => loader.EnsureLoadedAsync(42));

        cut.WaitForAssertion(() =>
        {
            var owner = Services.GetRequiredService<BreadcrumbService>().Items[1];
            Assert.Equal("Atlas", owner.Text);
            Assert.False(owner.IsLoading);
        });
    }
}
