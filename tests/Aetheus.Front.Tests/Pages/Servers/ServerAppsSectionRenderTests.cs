// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Pages.Servers.ServerDetailSections;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Bunit;
using Radzen;

namespace Aetheus.Front.Tests.Pages.Servers;

public class ServerAppsSectionRenderTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;
    private static readonly BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly BindingFlags PrivStatic = BindingFlags.NonPublic | BindingFlags.Static;
    private static readonly Type SectionType = typeof(ServerAppsSection);

    public ServerAppsSectionRenderTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
        _handler.SetPaginatedJsonResponse("api/servers/1/apps", new List<ServerAppDto>());
    }

    [Fact]
    public void Renders_WithNoApps()
    {
        var cut = Render<ServerAppsSection>(p => p.Add(x => x.ServerId, 1));
        // After the fetch resolves, the section header and empty-grid text render.
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Applications", cut.Markup);
            Assert.Contains("NoRecords", cut.Markup);
        });
    }

    [Fact]
    public void Renders_WithApps()
    {
        _handler.SetPaginatedJsonResponse("api/servers/1/apps", new List<ServerAppDto>
        {
            new() { Id = 1, Name = "nginx", Version = "1.21", Port = 80, Source = "systemd", Status = ServerAppStatus.Running },
            new() { Id = 2, Name = "redis", Version = "6.2", Port = 6379, Source = "docker", Status = ServerAppStatus.Stopped },
            new() { Id = 3, Name = "broken-app", Source = "manual", Status = ServerAppStatus.Error }
        });
        var cut = Render<ServerAppsSection>(p => p.Add(x => x.ServerId, 1));
        // After the fetch resolves, the seeded app names render in the grid.
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("nginx", cut.Markup);
            Assert.Contains("redis", cut.Markup);
            Assert.Contains("broken-app", cut.Markup);
        });
    }

    [Fact]
    public void AddApp_InitiallyNotSaving()
    {
        var cut = Render<ServerAppsSection>(p => p.Add(x => x.ServerId, 1));
        var saving = (bool)SectionType.GetField("_addSaving", Priv)!.GetValue(cut.Instance)!;
        Assert.False(saving);
    }

    [Fact]
    public void AddApp_InitialDefaultSource_IsManual()
    {
        var cut = Render<ServerAppsSection>(p => p.Add(x => x.ServerId, 1));
        var source = (string)SectionType.GetField("_addSource", Priv)!.GetValue(cut.Instance)!;
        Assert.Equal("manual", source);
    }

    [Theory]
    [InlineData(ServerAppStatus.Running, BadgeStyle.Success)]
    [InlineData(ServerAppStatus.Stopped, BadgeStyle.Light)]
    [InlineData(ServerAppStatus.Error, BadgeStyle.Danger)]
    public void GetAppStatusBadge_ReturnsExpected(ServerAppStatus status, BadgeStyle expected)
    {
        var method = SectionType.GetMethod("GetAppStatusBadge", PrivStatic)!;
        var result = (BadgeStyle)method.Invoke(null, [status])!;
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetAppStatusBadge_Unknown_ReturnsWarning()
    {
        var method = SectionType.GetMethod("GetAppStatusBadge", PrivStatic)!;
        var result = (BadgeStyle)method.Invoke(null, [(ServerAppStatus)99])!;
        Assert.Equal(BadgeStyle.Warning, result);
    }

    [Fact]
    public void Sources_ContainsExpectedValues()
    {
        var field = SectionType.GetField("_sources", PrivStatic)!;
        var sources = (string[])field.GetValue(null)!;
        Assert.Contains("manual", sources);
        Assert.Contains("systemd", sources);
        Assert.Contains("docker", sources);
    }

    [Fact]
    public void AddVisible_DefaultFalse()
    {
        var cut = Render<ServerAppsSection>(p => p.Add(x => x.ServerId, 1));
        var addVisible = (bool)SectionType.GetField("_addVisible", Priv)!.GetValue(cut.Instance)!;
        Assert.False(addVisible);
    }
}
