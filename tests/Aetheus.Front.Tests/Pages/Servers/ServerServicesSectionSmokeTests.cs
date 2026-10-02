// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Components.Servers.ServerDetailSections;
using Bunit;

namespace Aetheus.Front.Tests.Pages.Servers;

public class ServerServicesSectionSmokeTests : BunitContext
{
    public ServerServicesSectionSmokeTests()
    {
        BunitTestHelper.RegisterServices(this);
    }

    [Fact]
    public void Renders_ServicesSection_NoServices_ShowsHeadingAndEmptyState()
    {
        var server = new ServerDetailDto { Id = 1, Name = "web-01", Services = [] };
        var cut = Render<ServerServicesSection>(p => p
            .Add(x => x.Server, server)
            .Add(x => x.ServerId, server.Id));
        // The section header always renders, and with no installed services the
        // manageable tab shows the NoServicesToDisplay empty-state.
        Assert.Contains("Services", cut.Markup);
        Assert.Contains("NoServicesToDisplay", cut.Markup);
    }

    [Fact]
    public void Renders_ServicesSection_WithService_ShowsServiceName()
    {
        var server = new ServerDetailDto
        {
            Id = 1,
            Name = "web-01",
            Services =
            [
                new ServiceInfoDto
                {
                    Name = "docker",
                    Status = "active (running)",
                    IsRunning = true,
                    IsInstalled = true,
                    IsManageable = true,
                    Type = ServiceType.Systemd
                }
            ]
        };
        var cut = Render<ServerServicesSection>(p => p
            .Add(x => x.Server, server)
            .Add(x => x.ServerId, server.Id));
        Assert.Contains("docker", cut.Markup);
    }
}
