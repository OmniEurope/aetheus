// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace Aetheus.Back.Tests;

public class MonitoringControllerIntegrationTests(CustomWebApplicationFactory factory)
    : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = CreateAuthenticatedClient(factory);

    // ──── Dashboard ────

    [Fact]
    public async Task GetDashboard_Returns200WithData()
    {
        var response = await _client.GetAsync("/api/monitoring/dashboard", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dashboard = await response.Content.ReadFromJsonAsync<DashboardOverviewDto>(TestJsonOptions.Default, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(dashboard);
        Assert.True(dashboard.TotalServers >= 0);
        Assert.True(dashboard.OnlineServers >= 0);
        Assert.True(dashboard.OfflineServers >= 0);
    }

    [Fact]
    public async Task GetDashboard_AfterSeedingServers_ReflectsCounts()
    {
        await SeedServerAsync("mon-server-1", ServerStatus.Online);
        await SeedServerAsync("mon-server-2", ServerStatus.Offline);

        var dashboard = await _client.GetFromJsonAsync<DashboardOverviewDto>("/api/monitoring/dashboard", TestJsonOptions.Default, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(dashboard);
        Assert.True(dashboard.TotalServers >= 2);
        Assert.True(dashboard.OnlineServers >= 1);
        Assert.True(dashboard.OfflineServers >= 1);
    }

    // ──── Server Metrics ────

    [Fact]
    public async Task GetServerMetrics_ExistingServer_Returns200()
    {
        var serverId = await SeedServerAsync("metrics-server", ServerStatus.Online);
        await SeedMetricsAsync(serverId);

        var response = await _client.GetAsync($"/api/monitoring/servers/{serverId}/metrics?hours=24", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var metrics = await response.Content.ReadFromJsonAsync<List<ServerMetricDto>>(TestJsonOptions.Default, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(metrics);
        Assert.NotEmpty(metrics);
    }

    [Fact]
    public async Task GetServerMetrics_NoMetrics_ReturnsEmptyList()
    {
        var serverId = await SeedServerAsync("no-metrics-server", ServerStatus.Online);

        var metrics = await _client.GetFromJsonAsync<List<ServerMetricDto>>(
            $"/api/monitoring/servers/{serverId}/metrics?hours=24", TestJsonOptions.Default, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(metrics);
        Assert.Empty(metrics);
    }

    [Fact]
    public async Task GetServerMetrics_DefaultHours_Returns200()
    {
        var serverId = await SeedServerAsync("default-hours-server", ServerStatus.Online);

        var response = await _client.GetAsync($"/api/monitoring/servers/{serverId}/metrics", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ──── Auth ────

    [Fact]
    public async Task Unauthorized_NoToken_Returns401()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/api/monitoring/dashboard", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ──── Helpers ────

    private async Task<int> SeedServerAsync(string name, ServerStatus status)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var existing = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .FirstOrDefaultAsync(db.Servers, s => s.Name == name);
        if (existing is not null) return existing.Id;

        var server = new Server
        {
            Name = name,
            Type = ServerType.Normal,
            Status = status,
            IpAddress = "10.0.0.70",
            AgentVersion = "1.0.0",
            OsDescription = "Linux",
            LastHeartbeat = DateTime.UtcNow
        };
        db.Servers.Add(server);
        await db.SaveChangesAsync();
        return server.Id;
    }

    private async Task SeedMetricsAsync(int serverId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.ServerMetrics.AddRange(
            new ServerMetric
            {
                ServerId = serverId,
                CpuPercent = 42.5,
                MemoryUsedMb = 2048,
                MemoryTotalMb = 8192,
                DiskUsedGb = 50,
                DiskTotalGb = 200,
                Timestamp = DateTime.UtcNow.AddMinutes(-10)
            },
            new ServerMetric
            {
                ServerId = serverId,
                CpuPercent = 55.0,
                MemoryUsedMb = 3072,
                MemoryTotalMb = 8192,
                DiskUsedGb = 51,
                DiskTotalGb = 200,
                Timestamp = DateTime.UtcNow.AddMinutes(-5)
            });
        await db.SaveChangesAsync();
    }

    private static HttpClient CreateAuthenticatedClient(CustomWebApplicationFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CustomWebApplicationFactory.GenerateTestToken());
        return client;
    }
}
