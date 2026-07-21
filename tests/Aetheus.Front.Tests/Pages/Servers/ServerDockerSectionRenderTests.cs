// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Pages.Servers.ServerDetailSections;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Bunit;

namespace Aetheus.Front.Tests.Pages.Servers;

public class ServerDockerSectionRenderTests : BunitContext
{
    public ServerDockerSectionRenderTests() => BunitTestHelper.RegisterServices(this, authenticated: true, isAdmin: true);

    private static ServerDetailDto ServerWith(params DockerContainerDto[] containers) => new()
    {
        Id = 1,
        Name = "web-01",
        Hostname = "web-01.local",
        DockerAvailable = true,
        Docker = new DockerDataDto { Containers = [.. containers] }
    };

    private static DockerContainerDto Container(string name, string state = "running") => new()
    {
        ContainerId = $"id-{name}",
        Name = name,
        Image = "nginx:latest",
        State = state,
        Status = "Up 2 hours",
        Ports = "80/tcp",
        CpuPercent = 1.5,
        MemoryUsageMb = 64,
        MemoryLimitMb = 512
    };

    [Fact]
    public void Renders_ContainerList_ShowsContainerName()
    {
        var cut = Render<ServerDockerSection>(p => p
            .Add(c => c.Server, ServerWith(Container("nginx-proxy"), Container("redis-cache", "exited")))
            .Add(c => c.ServerId, 1)
            .Add(c => c.DockerInitialLoaded, true));

        Assert.Contains("nginx-proxy", cut.Markup);
        Assert.Contains("redis-cache", cut.Markup);
    }

    [Fact]
    public void Renders_NoContainers_WithoutError()
    {
        var cut = Render<ServerDockerSection>(p => p
            .Add(c => c.Server, ServerWith())
            .Add(c => c.ServerId, 1)
            .Add(c => c.DockerInitialLoaded, true));

        Assert.DoesNotContain("nginx-proxy", cut.Markup);
    }
}
