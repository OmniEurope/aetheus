// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Pages.Environments;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Aetheus.Front.Tests;

public class EnvironmentEditTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public EnvironmentEditTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
    }

    private void SetupDefaultResponses()
    {
        _handler.SetJsonResponse("api/projects", new PaginatedResult<ProjectDto>
        {
            Items = [new ProjectDto { Id = 1, Name = "P1" }],
            TotalCount = 1
        });
        _handler.SetJsonResponse("api/servers", new PaginatedResult<ServerDto>
        {
            Items = [new ServerDto { Id = 1, Name = "web-01" }],
            TotalCount = 1
        });
    }

    [Fact]
    public void Renders_NewEnvironmentForm()
    {
        SetupDefaultResponses();

        var cut = Render<EnvironmentEdit>();

        Assert.Contains("NewEnvironment", cut.Markup);
    }

    [Fact]
    public void Renders_EditMode_LoadsExisting()
    {
        SetupDefaultResponses();
        _handler.SetJsonResponse("api/environments/1", new EnvironmentDto
        {
            Id = 1,
            Name = "Prod",
            Description = "Production env",
            Type = EnvironmentType.Production,
            Servers = []
        });

        var cut = Render<EnvironmentEdit>(p => p.Add(x => x.Id, 1));

        // Edit mode renders the loaded environment's name in the header.
        Assert.Contains("Prod", cut.Markup);
    }

    [Fact]
    public void Renders_FormFields()
    {
        SetupDefaultResponses();

        var cut = Render<EnvironmentEdit>();

        Assert.Contains("Name", cut.Markup);
        Assert.Contains("Description", cut.Markup);
        Assert.Contains("Type", cut.Markup);
    }

    [Fact]
    public void Renders_DeleteButton_InEditMode()
    {
        SetupDefaultResponses();
        _handler.SetJsonResponse("api/environments/1", new EnvironmentDto
        {
            Id = 1,
            Name = "Prod",
            Type = EnvironmentType.Production,
            Servers = []
        });

        var cut = Render<EnvironmentEdit>(p => p.Add(x => x.Id, 1));

        Assert.Contains("Delete", cut.Markup);
    }

    [Fact]
    public void DoesNotRender_DeleteButton_InNewMode()
    {
        SetupDefaultResponses();

        var cut = Render<EnvironmentEdit>();

        var markup = cut.Markup;
        Assert.DoesNotContain("delete", markup.ToLowerInvariant().Replace("localizeddata", "").Replace("deleteenvironment", ""));
    }

    [Fact]
    public async Task NewEnvironment_ProjectIdQueryChange_RebindsModel()
    {
        _handler.SetJsonResponse("api/projects", new PaginatedResult<ProjectDto>
        {
            Items = [new ProjectDto { Id = 1, Name = "P1" }, new ProjectDto { Id = 2, Name = "P2" }],
            TotalCount = 2
        });
        _handler.SetJsonResponse("api/servers", new PaginatedResult<ServerDto> { Items = [], TotalCount = 0 });
        Services.GetRequiredService<NavigationManager>().NavigateTo("http://test/environments/new?projectId=1");
        var cut = Render<EnvironmentEdit>();

        cut.Instance.ProjectId = 2;
        var method = typeof(EnvironmentEdit).GetMethod(
            "OnParametersSetAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await cut.InvokeAsync(() => (Task)method.Invoke(cut.Instance, [])!);

        var model = typeof(EnvironmentEdit).GetField("_model", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(cut.Instance)!;
        Assert.Equal(2, model.GetType().GetProperty("ProjectId")!.GetValue(model));
    }
}
