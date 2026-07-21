// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Pages.Projects.ProjectDetailSections;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Bunit;
using Radzen;

namespace Aetheus.Front.Tests.Pages;

public class ProjectServersSectionMethodTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public ProjectServersSectionMethodTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
    }

    private IRenderedComponent<ProjectServersSection> RenderSection()
    {
        _handler.SetPaginatedJsonResponse("api/projects/1/servers", new List<ProjectServerDto>
        {
            new() { Id = 1, DisplayName = "Web Server", Host = "10.0.0.1", Port = 80, Type = ProjectServerType.ExternalHost },
            new() { Id = 2, DisplayName = "DB Server", Host = "10.0.0.2", Type = ProjectServerType.AgentServer, ServerStatus = ServerStatus.Online }
        });
        _handler.SetJsonResponse("api/servers", new PaginatedResult<ServerDto>
        {
            Items = [new ServerDto { Id = 10, Name = "agent-1", Hostname = "10.0.0.10" }],
            TotalCount = 1
        });

        return Render<ProjectServersSection>(p => p.Add(x => x.ProjectId, 1));
    }

    [Theory]
    [InlineData(ProjectServerType.AgentServer, BadgeStyle.Info)]
    [InlineData(ProjectServerType.ExternalHost, BadgeStyle.Warning)]
    public void GetTypeBadge_ReturnsExpected(ProjectServerType type, BadgeStyle expected)
    {
        var method = typeof(ProjectServersSection).GetMethod("GetTypeBadge", BindingFlags.NonPublic | BindingFlags.Static)!;
        var result = (BadgeStyle)method.Invoke(null, [type])!;
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(ServerStatus.Online, BadgeStyle.Success)]
    [InlineData(ServerStatus.Offline, BadgeStyle.Danger)]
    [InlineData(null, BadgeStyle.Light)]
    public void GetStatusBadge_ReturnsExpected(ServerStatus? status, BadgeStyle expected)
    {
        var method = typeof(ProjectServersSection).GetMethod("GetStatusBadge", BindingFlags.NonPublic | BindingFlags.Static)!;
        var result = (BadgeStyle)method.Invoke(null, [status])!;
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Renders_WithServerData()
    {
        var cut = RenderSection();
        cut.WaitForState(() => cut.Markup.Contains("Web Server"), TimeSpan.FromSeconds(2));
        Assert.Contains("Web Server", cut.Markup);
        Assert.Contains("DB Server", cut.Markup);
    }
}
