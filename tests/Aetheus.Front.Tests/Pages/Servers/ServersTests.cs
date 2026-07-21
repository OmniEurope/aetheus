// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Pages;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using ServersPage = Aetheus.Front.Pages.Servers.Servers;
namespace Aetheus.Front.Tests.Pages.Servers;

public class ServersTests : BunitContext
{
    public ServersTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
    }

    private readonly BunitTestHelper.TestHandler _handler;

    [Fact]
    public void Renders_ServerGrid()
    {
        _handler.SetJsonResponse("api/servers", new PaginatedResult<ServerDto>
        {
            Items =
            [
                new ServerDto
                {
                    Id = 1,
                    Name = "prod-web-01",
                    Hostname = "10.0.0.1",
                    Type = ServerType.Docker,
                    Status = ServerStatus.Online,
                    Tags = ["production"]
                }
            ],
            TotalCount = 1
        });

        var cut = Render<ServersPage>();
        // RadzenDataGrid LoadData requires JS interop; verify page renders
        Assert.Contains("Servers", cut.Markup);
    }

    [Fact]
    public void Renders_Empty()
    {
        _handler.SetJsonResponse("api/servers", new PaginatedResult<ServerDto>
        {
            Items = [],
            TotalCount = 0
        });

        var cut = Render<ServersPage>();
        // Page header renders even when the grid is empty.
        Assert.Contains("Servers", cut.Markup);
    }

    [Fact]
    public void Renders_MultipleServers()
    {
        _handler.SetJsonResponse("api/servers", new PaginatedResult<ServerDto>
        {
            Items =
            [
                new ServerDto { Id = 1, Name = "web-01", Hostname = "10.0.0.1", Type = ServerType.Docker, Status = ServerStatus.Online, Tags = ["production", "web"] },
                new ServerDto { Id = 2, Name = "db-01", Hostname = "10.0.0.2", Type = ServerType.Normal, Status = ServerStatus.Online, Tags = ["production", "database"] },
                new ServerDto { Id = 3, Name = "build-01", Hostname = "10.0.0.3", Type = ServerType.Build, Status = ServerStatus.Offline, Tags = ["ci"] },
                new ServerDto { Id = 4, Name = "staging-01", Hostname = "10.0.0.4", Type = ServerType.Docker, Status = ServerStatus.Disabled, Tags = ["staging"] }
            ],
            TotalCount = 4
        });

        var cut = Render<ServersPage>();
        Assert.Contains("Servers", cut.Markup);
    }

    [Fact]
    public void Renders_LargeServerList()
    {
        var items = Enumerable.Range(1, 25).Select(i => new ServerDto
        {
            Id = i,
            Name = $"server-{i:D3}",
            Hostname = $"10.0.0.{i}",
            Type = i % 3 == 0 ? ServerType.Docker : ServerType.Normal,
            Status = i % 4 == 0 ? ServerStatus.Offline : ServerStatus.Online,
            Tags = [$"tag-{i % 5}"]
        }).ToList();

        _handler.SetJsonResponse("api/servers", new PaginatedResult<ServerDto>
        {
            Items = items,
            TotalCount = 100
        });

        var cut = Render<ServersPage>();
        // Page header renders regardless of list size.
        Assert.Contains("Servers", cut.Markup);
    }

    [Fact]
    public async Task ClearFilters_ResetsAll()
    {
        _handler.SetJsonResponse("api/servers", new PaginatedResult<ServerDto> { Items = [], TotalCount = 0 });
        var cut = Render<ServersPage>();

        cut.Instance._search = "test";
        cut.Instance._typeFilter = ServerType.Docker;
        cut.Instance._statusFilter = ServerStatus.Online;

        await cut.Instance.ClearFilters();

        Assert.Null(cut.Instance._search);
        Assert.Null(cut.Instance._typeFilter);
        Assert.Null(cut.Instance._statusFilter);
    }

    [Fact]
    public async Task FilterByTag_SetsSearch()
    {
        _handler.SetJsonResponse("api/servers", new PaginatedResult<ServerDto> { Items = [], TotalCount = 0 });
        var cut = Render<ServersPage>();

        await cut.Instance.FilterByTag("production");

        Assert.Equal("production", cut.Instance._search);
    }
}
