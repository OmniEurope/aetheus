// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Pages.Projects.ProjectDetailSections;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Bunit;
using Microsoft.AspNetCore.Components;

namespace Aetheus.Front.Tests.Pages;

public class ProjectEditSectionMethodTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public ProjectEditSectionMethodTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
        _handler.SetPaginatedJsonResponse<GitLightRepoDto>(
            HttpMethod.Get,
            "api/git/repos?page=1&pageSize=100&projectId=",
            []);
    }

    private static ProjectDetailDto MakeProject() => new()
    {
        Id = 1,
        Name = "TestProject",
        Description = "A test project",
        RepositoryUrl = "https://github.com/test/repo",
        DefaultBranch = "main",
        Status = ProjectStatus.Active,
        Tags = ["tag1", "tag2"]
    };

    [Fact]
    public void OnParametersSet_PopulatesModel()
    {
        var cut = Render<ProjectEditSection>(p => p.Add(x => x.Project, MakeProject()));

        var model = typeof(ProjectEditSection)
            .GetField("_model", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(cut.Instance)!;

        var name = (string)model.GetType().GetProperty("Name")!.GetValue(model)!;
        Assert.Equal("TestProject", name);

        var tagsRaw = (string)model.GetType().GetProperty("TagsRaw")!.GetValue(model)!;
        Assert.Contains("tag1", tagsRaw);
        Assert.Contains("tag2", tagsRaw);
    }

    [Fact]
    public async Task OnSubmit_Success_CallsOnSaved()
    {
        var savedCalled = false;
        _handler.SetJsonResponse("api/projects/1", new ProjectDetailDto { Id = 1, Name = "Updated" });

        var cut = Render<ProjectEditSection>(p =>
        {
            p.Add(x => x.Project, MakeProject());
            p.Add(x => x.OnSaved, EventCallback.Factory.Create(this, () => savedCalled = true));
        });

        var method = typeof(ProjectEditSection).GetMethod("OnSubmit", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance, [])!);

        Assert.True(savedCalled);
    }

    [Fact]
    public void Renders_WithProject()
    {
        var cut = Render<ProjectEditSection>(p => p.Add(x => x.Project, MakeProject()));
        // The edit form renders with the project's name bound into the Name field.
        Assert.Contains("EditProject", cut.Markup);
        Assert.Contains("TestProject", cut.Markup);
    }
}
