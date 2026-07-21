// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Pages.Projects;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Aetheus.Front.Tests.Pages.Projects;

public class ProjectEditDeepTests : BunitContext
{
    private static readonly BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;
    private readonly BunitTestHelper.TestHandler _handler;

    public ProjectEditDeepTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
    }

    [Fact]
    public void Renders_NewProject_Form()
    {
        // No Id = new project form
        var cut = Render<ProjectEdit>();

        // New-project mode: _isNew is true and the page does NOT redirect to an overview route.
        var isNew = (bool)typeof(ProjectEdit).GetProperty("_isNew", Priv)!.GetValue(cut.Instance)!;
        Assert.True(isNew);
        var nav = Services.GetRequiredService<Bunit.TestDoubles.BunitNavigationManager>();
        Assert.DoesNotContain("/overview", nav.Uri);
    }

    [Fact]
    public void Renders_WithNullId_AsNewProject()
    {
        var cut = Render<ProjectEdit>(p => p.Add(x => x.Id, (int?)null));
        var isNew = typeof(ProjectEdit).GetProperty("_isNew", Priv)?
            .GetValue(cut.Instance) as bool?;
        // A null Id puts the component in new-project mode (_isNew == true).
        Assert.True(isNew);
    }

    [Fact]
    public void ExistingProjectId_RedirectsToOverview()
    {
        var cut = Render<ProjectEdit>(p => p.Add(x => x.Id, 5));
        var nav = Services.GetRequiredService<Bunit.TestDoubles.BunitNavigationManager>();
        Assert.Contains("projects/5", nav.Uri);
    }

    [Fact]
    public void ProjectModel_DefaultState_HasEmptyName()
    {
        var cut = Render<ProjectEdit>(p => p.Add(x => x.Id, (int?)null));
        var model = typeof(ProjectEdit).GetField("_model", Priv)!.GetValue(cut.Instance);
        Assert.NotNull(model);
        var name = model!.GetType().GetProperty("Name")!.GetValue(model) as string;
        Assert.Equal(string.Empty, name);
    }

    [Fact]
    public void ProjectModel_DefaultStatus_IsActive()
    {
        var cut = Render<ProjectEdit>(p => p.Add(x => x.Id, (int?)null));
        var model = typeof(ProjectEdit).GetField("_model", Priv)!.GetValue(cut.Instance)!;
        var status = (ProjectStatus)model.GetType().GetProperty("Status")!.GetValue(model)!;
        Assert.Equal(ProjectStatus.Active, status);
    }

    [Fact]
    public async Task OnSubmit_WithCreatedProject_NavigatesToProject()
    {
        var created = new ProjectDetailDto { Id = 99, Name = "New Project" };
        _handler.SetJsonResponse("api/projects", created);

        var cut = Render<ProjectEdit>(p => p.Add(x => x.Id, (int?)null));
        var model = typeof(ProjectEdit).GetField("_model", Priv)!.GetValue(cut.Instance)!;
        model.GetType().GetProperty("Name")!.SetValue(model, "New Project");

        var method = typeof(ProjectEdit).GetMethod("OnSubmit", Priv)!;
        await cut.InvokeAsync(() => (Task)method.Invoke(cut.Instance, [])!);

        // On success OnSubmit navigates to the created project's page (id from the POST response).
        Assert.Contains(_handler.Requests, r => r.Method == "POST" && r.Url.Contains("api/projects"));
        var nav = Services.GetRequiredService<Bunit.TestDoubles.BunitNavigationManager>();
        Assert.Equal("http://localhost/projects/99", nav.Uri);
    }

    [Fact]
    public void Saving_InitiallyFalse()
    {
        var cut = Render<ProjectEdit>(p => p.Add(x => x.Id, (int?)null));
        var saving = (bool)typeof(ProjectEdit).GetField("_saving", Priv)!.GetValue(cut.Instance)!;
        Assert.False(saving);
    }
}
