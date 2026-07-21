// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Aetheus.Back.Tests;

public class TasksControllerIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string AgentToken = "test-agent-token-for-integration";
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public TasksControllerIntegrationTests(CustomWebApplicationFactory factory)
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
    public async Task GetTasks_ReturnsPaginatedResult()
    {
        var result = await _client.GetFromJsonAsync<PaginatedResult<ServerTaskDto>>(
            "/api/tasks?page=1&pageSize=10", TestJsonOptions.Default, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.True(result.Page >= 1);
        Assert.Equal(10, result.PageSize);
    }

    [Fact]
    public async Task CreateTask_ValidRequest_Returns201()
    {
        var serverId = await SeedServerAsync();

        var request = new CreateTaskRequest
        {
            ServerId = serverId,
            Name = "Test Task",
            Command = "echo hello"
        };

        var response = await _client.PostAsJsonAsync("/api/tasks", request, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var task = await response.Content.ReadFromJsonAsync<ServerTaskDto>(TestJsonOptions.Default, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(task);
        Assert.Equal("Test Task", task.Name);
        Assert.True(task.Id > 0);
    }

    [Fact]
    public async Task GetTask_AfterCreate_ReturnsTask()
    {
        var created = await CreateTestTaskAsync();

        var task = await _client.GetFromJsonAsync<ServerTaskDto>($"/api/tasks/{created.Id}", TestJsonOptions.Default, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(task);
        Assert.Equal(created.Id, task.Id);
    }

    [Fact]
    public async Task GetTask_NonExistent_Returns404()
    {
        var response = await _client.GetAsync("/api/tasks/99999", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task StartTask_AfterCreate_Returns200()
    {
        var created = await CreateTestTaskAsync();
        var agentClient = CreateAgentClient();

        var response = await agentClient.PostAsync($"/api/tasks/{created.Id}/start", null, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CompleteTask_AfterStart_Returns200()
    {
        var created = await CreateTestTaskAsync();
        var agentClient = CreateAgentClient();
        await agentClient.PostAsync($"/api/tasks/{created.Id}/start", null, cancellationToken: TestContext.Current.CancellationToken);

        var result = new TaskResultDto
        {
            TaskId = created.Id,
            Status = TaskExecutionStatus.Success,
            ExitCode = 0,
            Output = "hello"
        };

        var response = await agentClient.PostAsJsonAsync($"/api/tasks/{created.Id}/complete", result, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CancelTask_AfterCreate_Returns200()
    {
        var created = await CreateTestTaskAsync();

        var response = await _client.PostAsync($"/api/tasks/{created.Id}/cancel", null, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CancelTask_NonExistent_Returns404()
    {
        var response = await _client.PostAsync("/api/tasks/99999/cancel", null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ClaimPendingTasks_Returns200()
    {
        var serverId = await SeedServerAsync();
        var agentClient = CreateAgentClient();

        var response = await agentClient.PostAsync($"/api/tasks/claim?serverId={serverId}", null, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Unauthorized_NoToken_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/tasks", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<int> SeedServerAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // Idempotent by the canonical name - NOT "any server exists": DbInitializer and other tests
        // may seed servers without this agent token, so returning an arbitrary first server made the
        // agent-token ownership check order-dependent once xunit.v3 reordered the tests.
        var existing = await db.Servers.FirstOrDefaultAsync(s => s.Name == "task-test-server");
        if (existing is not null)
            return existing.Id;

        var hmacKey = System.Text.Encoding.UTF8.GetBytes("aetheus-dev-key-minimum-32-bytes!!");
        var tokenHash = Convert.ToBase64String(System.Security.Cryptography.HMACSHA256.HashData(
            hmacKey, System.Text.Encoding.UTF8.GetBytes(AgentToken)));

        var server = new Server
        {
            Name = "task-test-server",
            Type = ServerType.Normal,
            Status = ServerStatus.Online,
            IpAddress = "10.0.0.10",
            AgentVersion = "1.0.0",
            OsDescription = "Linux",
            LastHeartbeat = DateTime.UtcNow
        };
        db.Servers.Add(server);
        await db.SaveChangesAsync();

        db.ServerTokens.RemoveRange(db.ServerTokens.Where(t => t.TokenHash == tokenHash));
        db.ServerTokens.Add(new ServerToken
        {
            ServerId = server.Id,
            TokenHash = tokenHash,
            ExpiresAt = DateTime.UtcNow.AddDays(30)
        });
        await db.SaveChangesAsync();

        return server.Id;
    }

    private async Task<ServerTaskDto> CreateTestTaskAsync()
    {
        var serverId = await SeedServerAsync();
        var response = await _client.PostAsJsonAsync("/api/tasks", new CreateTaskRequest
        {
            ServerId = serverId,
            Name = "IntegrationTask",
            Command = "echo test"
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ServerTaskDto>(TestJsonOptions.Default))!;
    }
}
