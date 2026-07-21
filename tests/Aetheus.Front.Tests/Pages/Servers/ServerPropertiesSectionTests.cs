// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Pages.Servers.ServerDetailSections;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Bunit;

namespace Aetheus.Front.Tests.Pages.Servers;

public class ServerPropertiesSectionTests : BunitContext
{
    public ServerPropertiesSectionTests()
    {
        BunitTestHelper.RegisterServices(this);
    }

    [Fact]
    public void Renders_PropertiesSection_ShowsServerFields()
    {
        var server = new ServerDetailDto
        {
            Id = 1,
            Name = "web-01",
            Status = ServerStatus.Online,
            Hostname = "192.168.1.1",
            Type = ServerType.Normal
        };
        var cut = Render<ServerPropertiesSection>(p => p.Add(x => x.Server, server));
        // The properties card renders the actual server fields, not just an empty shell.
        Assert.Contains("web-01", cut.Markup);
        Assert.Contains("192.168.1.1", cut.Markup);
        Assert.Contains("Normal", cut.Markup);   // Type badge text
        Assert.Contains("Online", cut.Markup);   // Status badge text
    }

    [Fact]
    public void Renders_PropertiesSection_OfflineServer_ShowsOfflineStatus()
    {
        var server = new ServerDetailDto
        {
            Id = 2,
            Name = "db-02",
            Status = ServerStatus.Offline,
            Hostname = "10.0.0.2",
            Type = ServerType.Docker
        };
        var cut = Render<ServerPropertiesSection>(p => p.Add(x => x.Server, server));
        Assert.Contains("Offline", cut.Markup);
        Assert.Contains("Docker", cut.Markup);
    }
}
