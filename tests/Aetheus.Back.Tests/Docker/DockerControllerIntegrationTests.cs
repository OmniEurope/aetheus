// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Aetheus.Back.Tests;

public class DockerControllerIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public DockerControllerIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = _factory.CreateClient();
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CustomWebApplicationFactory.GenerateTestToken());
    }

    private async Task SeedServerAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (!await db.Servers.AnyAsync())
        {
            db.Servers.Add(new Server
            {
                Id = 1,
                Name = "test-server",
                Type = ServerType.Docker,
                Status = ServerStatus.Online,
                IpAddress = "10.0.0.1",
                AgentVersion = "1.0.0",
                OsDescription = "Linux",
                LastHeartbeat = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task GetContainers_NoServer_Returns404()
    {
        var response = await _client.GetAsync("api/servers/999/docker/containers", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetContainers_WithServer_Returns200()
    {
        await SeedServerAsync();
        var response = await _client.GetAsync("api/servers/1/docker/containers", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetImages_WithServer_Returns200()
    {
        await SeedServerAsync();
        var response = await _client.GetAsync("api/servers/1/docker/images", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetNetworks_WithServer_Returns200()
    {
        await SeedServerAsync();
        var response = await _client.GetAsync("api/servers/1/docker/networks", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetVolumes_WithServer_Returns200()
    {
        await SeedServerAsync();
        var response = await _client.GetAsync("api/servers/1/docker/volumes", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetComposeStacks_WithServer_Returns200()
    {
        await SeedServerAsync();
        var response = await _client.GetAsync("api/servers/1/docker/compose", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ExecuteAction_ValidRequest_Returns200()
    {
        await SeedServerAsync();
        var request = new DockerActionRequest
        {
            ContainerId = "abc123def456",
            Action = DockerContainerAction.Stop
        };
        var response = await _client.PostAsJsonAsync("api/servers/1/docker/action", request, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ExecuteAction_InvalidContainerId_ReturnsBadRequest()
    {
        await SeedServerAsync();
        var request = new DockerActionRequest
        {
            ContainerId = "invalid; rm -rf /",
            Action = DockerContainerAction.Stop
        };
        var response = await _client.PostAsJsonAsync("api/servers/1/docker/action", request, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task InspectContainer_ValidId_Returns200()
    {
        await SeedServerAsync();
        var response = await _client.PostAsync("api/servers/1/docker/containers/abc123def456/inspect", null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ExecuteShellCommand_ValidRequest_Returns200()
    {
        await SeedServerAsync();
        var request = new DockerExecRequest
        {
            ContainerId = "abc123def456",
            Command = "ls -la"
        };
        var response = await _client.PostAsJsonAsync("api/servers/1/docker/exec", request, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Prune_NothingSelected_ReturnsBadRequest()
    {
        await SeedServerAsync();
        var request = new DockerPruneRequest { Containers = false, Images = false, Volumes = false };
        var response = await _client.PostAsJsonAsync("api/servers/1/docker/prune", request, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Prune_ValidRequest_Returns200()
    {
        await SeedServerAsync();
        var request = new DockerPruneRequest { Containers = true, Images = true, Volumes = false };
        var response = await _client.PostAsJsonAsync("api/servers/1/docker/prune", request, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetContainerEnvVars_ValidId_Returns200()
    {
        await SeedServerAsync();
        var response = await _client.PostAsync("api/servers/1/docker/containers/abc123def456/env", null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ListContainerFiles_ValidRequest_Returns200()
    {
        await SeedServerAsync();
        var request = new DockerBrowseRequest { ContainerId = "abc123def456", Path = "/" };
        var response = await _client.PostAsJsonAsync("api/servers/1/docker/containers/browse", request, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task BuildImage_ValidRequest_Returns200()
    {
        await SeedServerAsync();
        var request = new DockerBuildRequest
        {
            ImageTag = "myapp:latest",
            DockerfileContent = "FROM alpine:latest\nRUN echo hello"
        };
        var response = await _client.PostAsJsonAsync("api/servers/1/docker/build", request, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetContainerLogs_ValidRequest_Returns200()
    {
        await SeedServerAsync();
        var request = new DockerContainerLogsRequest
        {
            ContainerId = "abc123def456",
            Tail = 50
        };
        var response = await _client.PostAsJsonAsync("api/servers/1/docker/containers/logs", request, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PullImage_ValidRequest_Returns200()
    {
        await SeedServerAsync();
        var request = new DockerPullImageRequest { Image = "alpine:latest" };
        var response = await _client.PostAsJsonAsync("api/servers/1/docker/images/pull", request, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RemoveImage_ValidId_Returns200()
    {
        await SeedServerAsync();
        var response = await _client.DeleteAsync("api/servers/1/docker/images/abc123def456", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ExecuteComposeAction_ValidRequest_Returns200()
    {
        await SeedServerAsync();
        var request = new DockerComposeActionRequest
        {
            StackName = "my-stack",
            Action = DockerComposeAction.Up
        };
        var response = await _client.PostAsJsonAsync("api/servers/1/docker/compose/action", request, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UpdateResourceLimits_ValidRequest_Returns200()
    {
        await SeedServerAsync();
        var request = new DockerResourceLimitsRequest
        {
            ContainerId = "abc123def456",
            CpuLimit = 2.0,
            MemoryLimitMb = 512
        };
        var response = await _client.PostAsJsonAsync("api/servers/1/docker/resource-limits", request, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetComposeFile_ValidStackName_Returns200()
    {
        await SeedServerAsync();
        var response = await _client.GetAsync("api/servers/1/docker/compose/my-stack/file", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SaveComposeFile_ValidRequest_Returns200()
    {
        await SeedServerAsync();
        var request = new DockerComposeFileSaveRequest
        {
            StackName = "my-stack",
            Content = "version: '3'\nservices:\n  web:\n    image: nginx"
        };
        var response = await _client.PutAsJsonAsync("api/servers/1/docker/compose/file", request, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Unauthorized_NoToken_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("api/servers/1/docker/containers", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
