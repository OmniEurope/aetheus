// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Pages.Servers.ServerDetailSections;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Bunit;

namespace Aetheus.Front.Tests.Pages.Servers;

public class ServerServicesSectionNewTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public ServerServicesSectionNewTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
    }

    private static ServerDetailDto MakeServer(params ServiceInfoDto[] services) => new()
    {
        Id = 21,
        Name = "services-test",
        Hostname = "10.0.0.21",
        Type = ServerType.Normal,
        Status = ServerStatus.Online,
        Tags = [],
        Services = services.ToList()
    };

    private static ServiceInfoDto Svc(string name, bool installed = true, bool manageable = true, bool running = true,
        ServiceType type = ServiceType.Systemd) => new()
        {
            Name = name,
            Status = running ? "active (running)" : "inactive (dead)",
            IsRunning = running,
            IsInstalled = installed,
            IsManageable = manageable,
            Type = type
        };

    private IRenderedComponent<ServerServicesSection> RenderSection(ServerDetailDto? server = null)
    {
        var s = server ?? MakeServer();
        return Render<ServerServicesSection>(p => p
            .Add(x => x.Server, s)
            .Add(x => x.ServerId, s.Id));
    }

    [Fact]
    public void Renders_ServicesHeader()
    {
        var cut = RenderSection();
        Assert.Contains("Services", cut.Markup);
    }

    [Fact]
    public void EmptyServer_ShowsNoServicesToDisplay()
    {
        var cut = RenderSection();
        Assert.Contains("NoServicesToDisplay", cut.Markup);
    }

    [Fact]
    public void WithDockerAndApache_RendersBothInModuleTab()
    {
        var cut = RenderSection(MakeServer(Svc("docker"), Svc("apache2")));
        Assert.Contains("docker", cut.Markup);
        Assert.Contains("apache2", cut.Markup);
    }

    [Fact]
    public void ModuleCount_ReflectsInstalledAndSyntheticModules()
    {
        var cut = RenderSection(MakeServer(Svc("docker"), Svc("apache2"), Svc("nginx")));
        // 2 installed (docker + apache2) + 5 synthetic missing (certbot, mail, teamspeak, portsentry, rkhunter) = 7
        Assert.Equal(7, cut.Instance.ModuleCount);
    }

    [Fact]
    public void OtherCount_ReflectsManageableWithoutModule()
    {
        var cut = RenderSection(MakeServer(Svc("nginx"), Svc("mysql"), Svc("docker")));
        Assert.Equal(2, cut.Instance.OtherCount);
    }

    [Fact]
    public void SystemCount_ReflectsUnmanageable()
    {
        var cut = RenderSection(MakeServer(
            Svc("systemd-journald", manageable: false),
            Svc("systemd-logind", manageable: false),
            Svc("docker")));
        Assert.Equal(2, cut.Instance.SystemCount);
    }

    [Theory]
    [InlineData("docker")]
    [InlineData("apache2")]
    [InlineData("postfix")]
    [InlineData("dovecot")]
    [InlineData("portsentry")]
    [InlineData("rkhunter")]
    [InlineData("certbot")]
    [InlineData("cron")]
    [InlineData("crond")]
    public void HasDedicatedModule_KnownServices_ReturnsTrue(string svc)
    {
        Assert.True(ServerServicesSection.HasDedicatedModule(svc));
    }

    [Theory]
    [InlineData("nginx")]
    [InlineData("mysql")]
    [InlineData("fail2ban")]
    public void HasDedicatedModule_OtherServices_ReturnsFalse(string svc)
    {
        Assert.False(ServerServicesSection.HasDedicatedModule(svc));
    }

    [Theory]
    [InlineData(ServiceType.Systemd, "Systemd")]
    [InlineData(ServiceType.Docker, "Docker")]
    [InlineData(ServiceType.WindowsService, "Windows")]
    public void FormatType_KnownTypes_ReturnsExpected(ServiceType type, string expected)
    {
        Assert.Equal(expected, ServerServicesSection.FormatType(type));
    }

    [Fact]
    public void ModuleInstalled_ReturnsOnlyInstalledWithModule()
    {
        var cut = RenderSection(MakeServer(
            Svc("docker", installed: true),
            Svc("apache2", installed: false),
            Svc("nginx", installed: true)));

        var list = cut.Instance.ModuleInstalled.ToList();
        Assert.Single(list);
        Assert.Equal("docker", list[0].Name);
    }
}
