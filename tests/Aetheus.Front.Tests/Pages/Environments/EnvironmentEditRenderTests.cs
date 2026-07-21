// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Pages.Environments;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Aetheus.Front.Tests.Pages.EnvironmentsTests;

public class EnvironmentEditRenderTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public EnvironmentEditRenderTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
    }

    private static EnvironmentDto BuildEnvironment() => new()
    {
        Id = 1,
        Name = "production",
        Description = "Main production environment",
        Type = EnvironmentType.Production,
        ProjectId = 1,
        ProjectName = "MyProject",
        RequireApproval = true,
        ApprovalTimeoutMinutes = 60,
        ApprovalInstructions = "Get approval from lead.",
        Servers = [new EnvironmentServerDto { ServerId = 5, ServerName = "web-01", ServerStatus = ServerStatus.Online }]
    };

    private static PaginatedResult<ProjectDto> BuildProjects() => new()
    {
        Items = [new ProjectDto { Id = 1, Name = "MyProject" }],
        TotalCount = 1
    };

    private static PaginatedResult<ServerDto> BuildServers() => new()
    {
        Items = [new ServerDto { Id = 5, Name = "web-01", Status = ServerStatus.Online, Type = ServerType.Normal }],
        TotalCount = 1
    };

    private void SetupStubs(int envId = 1)
    {
        _handler.SetJsonResponse("api/projects", BuildProjects());
        _handler.SetJsonResponse("api/servers", BuildServers());
        _handler.SetJsonResponse($"api/environments/{envId}", BuildEnvironment());
    }

    [Fact]
    public void Renders_ExistingEnvironment()
    {
        SetupStubs();

        var cut = Render<EnvironmentEdit>(p => p.Add(x => x.Id, 1));
        cut.WaitForState(() => cut.Markup.Length > 50, TimeSpan.FromSeconds(2));

        // Edit mode fetches environment 1 and populates the detail with its name.
        Assert.Contains(_handler.Requests, r => r.Url.Contains("api/environments/1"));
        var detail = (EnvironmentDto?)typeof(EnvironmentEdit)
            .GetField("_detail", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance);
        Assert.NotNull(detail);
        Assert.Equal("production", detail!.Name);
    }

    [Fact]
    public void Renders_NewEnvironment_WithoutId()
    {
        _handler.SetJsonResponse("api/projects", BuildProjects());
        _handler.SetJsonResponse("api/servers", BuildServers());

        var cut = Render<EnvironmentEdit>(p => p.Add(x => x.Id, (int?)null));
        cut.WaitForState(() => cut.Markup.Length > 50, TimeSpan.FromSeconds(2));

        // New-environment mode loads projects and servers but never fetches an environment detail.
        Assert.Contains(_handler.Requests, r => r.Url.Contains("api/projects"));
        Assert.Contains(_handler.Requests, r => r.Url.Contains("api/servers"));
        Assert.DoesNotContain(_handler.Requests, r => r.Url.Contains("api/environments/"));
    }

    [Fact]
    public void IsNew_IsTrueWhenIdIsNull()
    {
        _handler.SetJsonResponse("api/projects", BuildProjects());
        _handler.SetJsonResponse("api/servers", BuildServers());

        var cut = Render<EnvironmentEdit>(p => p.Add(x => x.Id, (int?)null));
        cut.WaitForState(() => cut.Markup.Length > 50, TimeSpan.FromSeconds(2));

        var isNew = (bool)typeof(EnvironmentEdit)
            .GetProperty("_isNew", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(cut.Instance)!;
        Assert.True(isNew);
    }

    [Fact]
    public void IsNew_IsFalseWhenIdIsSet()
    {
        SetupStubs();

        var cut = Render<EnvironmentEdit>(p => p.Add(x => x.Id, 1));
        cut.WaitForState(() => cut.Markup.Length > 50, TimeSpan.FromSeconds(2));

        var isNew = (bool)typeof(EnvironmentEdit)
            .GetProperty("_isNew", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(cut.Instance)!;
        Assert.False(isNew);
    }

    [Fact]
    public void TypeStyle_ReturnsCorrectBadgeStyles()
    {
        var method = typeof(EnvironmentEdit).GetMethod("TypeStyle", BindingFlags.NonPublic | BindingFlags.Static)!;

        Assert.Equal(Radzen.BadgeStyle.Danger, method.Invoke(null, [EnvironmentType.Production]));
        Assert.Equal(Radzen.BadgeStyle.Warning, method.Invoke(null, [EnvironmentType.Staging]));
        Assert.Equal(Radzen.BadgeStyle.Info, method.Invoke(null, [EnvironmentType.Testing]));
        Assert.Equal(Radzen.BadgeStyle.Success, method.Invoke(null, [EnvironmentType.Development]));
    }

    [Fact]
    public void Renders_EnvironmentWithApproval()
    {
        SetupStubs();

        var cut = Render<EnvironmentEdit>(p => p.Add(x => x.Id, 1));
        cut.WaitForState(() => cut.Markup.Length > 50, TimeSpan.FromSeconds(2));

        var detail = (EnvironmentDto?)typeof(EnvironmentEdit)
            .GetField("_detail", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(cut.Instance);
        Assert.NotNull(detail);
        Assert.True(detail.RequireApproval);
    }

    [Fact]
    public void Renders_NewEnvironment_WithProjectIdQuery()
    {
        _handler.SetJsonResponse("api/projects", BuildProjects());
        _handler.SetJsonResponse("api/servers", BuildServers());

        // ProjectId is [SupplyParameterFromQuery] - bUnit requires NavigationManager navigation
        // rather than p.Add() to supply query parameters. Navigate first, then render.
        var navManager = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        navManager.NavigateTo("http://localhost/?ProjectId=1");

        var cut = Render<EnvironmentEdit>(p => p.Add(x => x.Id, (int?)null));
        cut.WaitForState(() => cut.Markup.Length > 50, TimeSpan.FromSeconds(2));

        // The ?ProjectId=1 query matches a loaded project, so the new model pre-selects it.
        var model = typeof(EnvironmentEdit)
            .GetField("_model", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance)!;
        var projectId = (int?)model.GetType().GetProperty("ProjectId")!.GetValue(model);
        Assert.Equal(1, projectId);
    }
}
