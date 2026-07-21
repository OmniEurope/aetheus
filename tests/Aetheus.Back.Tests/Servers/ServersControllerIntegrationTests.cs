// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Aetheus.Back.Tests;

public class ServersControllerIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string AgentToken = "test-agent-token-for-servers";
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ServersControllerIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = _factory.CreateClient();
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CustomWebApplicationFactory.GenerateTestToken());
    }

    private HttpClient CreateAgentClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", AgentToken);
        return client;
    }

    [Fact]
    public async Task GetServers_ReturnsPaginatedResult()
    {
        var result = await _client.GetFromJsonAsync<PaginatedResult<ServerDto>>(
            "/api/servers?page=1&pageSize=10", TestJsonOptions.Default, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.True(result.Page >= 1);
        Assert.Equal(10, result.PageSize);
    }

    [Fact]
    public async Task GetServer_Existing_ReturnsDetail()
    {
        var serverId = await SeedServerAsync();

        var detail = await _client.GetFromJsonAsync<ServerDetailDto>($"/api/servers/{serverId}", TestJsonOptions.Default, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(detail);
        Assert.Equal(serverId, detail.Id);
        Assert.Equal("integration-server", detail.Name);
    }

    [Fact]
    public async Task GetServer_NonExistent_Returns404()
    {
        var response = await _client.GetAsync("/api/servers/99999", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateServer_ValidRequest_ReturnsUpdated()
    {
        var serverId = await SeedServerAsync();

        var response = await _client.PutAsJsonAsync($"/api/servers/{serverId}", new UpdateServerRequest
        {
            Name = "updated-server",
            Tags = ["prod", "web"]
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(response.IsSuccessStatusCode);
        var updated = await response.Content.ReadFromJsonAsync<ServerDto>(TestJsonOptions.Default, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(updated);
        Assert.Equal("updated-server", updated.Name);
    }

    [Fact]
    public async Task UpdateServer_NonExistent_Returns404()
    {
        var response = await _client.PutAsJsonAsync("/api/servers/99999", new UpdateServerRequest
        {
            Name = "X"
        }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteServer_Existing_Returns204()
    {
        var serverId = await SeedServerAsync("delete-me");

        var response = await _client.DeleteAsync($"/api/servers/{serverId}", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var getResponse = await _client.GetAsync($"/api/servers/{serverId}", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task DeleteServer_NonExistent_Returns404()
    {
        var response = await _client.DeleteAsync("/api/servers/99999", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Heartbeat_ValidRequest_Returns200()
    {
        var serverId = await SeedServerAsync();
        var agentClient = CreateAgentClient();

        var heartbeat = new ServerHeartbeatDto
        {
            CpuPercent = 42.5,
            MemoryUsedMb = 2048,
            MemoryTotalMb = 8192
        };

        var response = await agentClient.PostAsJsonAsync($"/api/servers/{serverId}/heartbeat", heartbeat, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetServers_WithTypeFilter_Returns200()
    {
        await SeedServerAsync();

        var result = await _client.GetFromJsonAsync<PaginatedResult<ServerDto>>(
            "/api/servers?type=Normal", TestJsonOptions.Default, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
    }

    [Fact]
    public async Task Unauthorized_NoToken_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/servers", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<int> SeedServerAsync(string name = "integration-server")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var existing = await db.Servers.FirstOrDefaultAsync(s => s.Name == name);
        if (existing is not null) return existing.Id;

        var server = new Server
        {
            Name = name,
            Type = ServerType.Normal,
            Status = ServerStatus.Online,
            IpAddress = "10.0.0.50",
            AgentVersion = "1.0.0",
            OsDescription = "Linux",
            LastHeartbeat = DateTime.UtcNow
        };
        db.Servers.Add(server);
        await db.SaveChangesAsync();

        // Only the canonical agent server carries the shared AgentToken hash. Attaching it to
        // other names (e.g. "delete-me") leaves several servers resolving the same token, and the
        // agent-token handler picks one by insertion order - which made the heartbeat ownership
        // check (agentServerId == routeId) order-dependent once xunit.v3 reordered the tests.
        if (name == "integration-server")
        {
            var hmacKey = Encoding.UTF8.GetBytes("aetheus-dev-key-minimum-32-bytes!!");
            var tokenHash = Convert.ToBase64String(HMACSHA256.HashData(
                hmacKey, Encoding.UTF8.GetBytes(AgentToken)));
            // Another test may have renamed the original agent server, orphaning its token; drop any
            // token with this hash so the agent token resolves to exactly this server (order-safe).
            db.ServerTokens.RemoveRange(db.ServerTokens.Where(t => t.TokenHash == tokenHash));
            db.ServerTokens.Add(new ServerToken
            {
                ServerId = server.Id,
                TokenHash = tokenHash,
                ExpiresAt = DateTime.UtcNow.AddDays(30)
            });
            await db.SaveChangesAsync();
        }

        return server.Id;
    }
}
