// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.Constants;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Aetheus.Back.Tests;

public class TasksControllerIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string AgentToken = "test-agent-token-for-integration";
    private const string AgentSessionId = "11111111111111111111111111111111";
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
        var claimed = await ClaimTaskAsync(agentClient, created.ServerId, created.Id);

        var response = await agentClient.PostAsJsonAsync(
            $"/api/tasks/{created.Id}/start",
            new AgentTaskLeaseRequest
            {
                AgentSessionId = AgentSessionId,
                AgentSessionFencingToken = claimed.AgentSessionFencingToken
            },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CompleteTask_AfterStart_Returns200()
    {
        var created = await CreateTestTaskAsync();
        var agentClient = CreateAgentClient();
        var claimed = await ClaimTaskAsync(agentClient, created.ServerId, created.Id);
        await agentClient.PostAsJsonAsync(
            $"/api/tasks/{created.Id}/start",
            new AgentTaskLeaseRequest
            {
                AgentSessionId = AgentSessionId,
                AgentSessionFencingToken = claimed.AgentSessionFencingToken
            },
            cancellationToken: TestContext.Current.CancellationToken);

        var result = new TaskResultDto
        {
            TaskId = created.Id,
            Status = TaskExecutionStatus.Success,
            ExitCode = 0,
            Output = "hello",
            AgentSessionId = AgentSessionId,
            AgentSessionFencingToken = claimed.AgentSessionFencingToken
        };

        var response = await agentClient.PostAsJsonAsync($"/api/tasks/{created.Id}/complete", result, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task LegacyAgent_CanStartAndCompleteClaimedTaskWithoutFencingFields()
    {
        var created = await CreateTestTaskAsync();
        await SetAgentVersionAsync(created.ServerId, "1.0.395");
        var agentClient = CreateAgentClient();
        await ClaimTaskAsync(agentClient, created.ServerId, created.Id);

        var start = await agentClient.PostAsJsonAsync(
            $"/api/tasks/{created.Id}/start",
            new { },
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);

        var complete = await agentClient.PostAsJsonAsync(
            $"/api/tasks/{created.Id}/complete",
            new TaskResultDto
            {
                TaskId = created.Id,
                Status = TaskExecutionStatus.Success,
                ExitCode = 0
            },
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);
    }

    [Fact]
    public async Task FencedAgent_CannotStartClaimedTaskWithoutFencingFields()
    {
        var created = await CreateTestTaskAsync();
        await SetAgentVersionAsync(created.ServerId, "1.0.761");
        var agentClient = CreateAgentClient();
        await ClaimTaskAsync(agentClient, created.ServerId, created.Id);

        var response = await agentClient.PostAsJsonAsync(
            $"/api/tasks/{created.Id}/start",
            new { },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
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

        var response = await agentClient.PostAsync(
            $"/api/tasks/claim?serverId={serverId}&agentSessionId={AgentSessionId}",
            null,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<PendingTaskDto> ClaimTaskAsync(
        HttpClient agentClient,
        int serverId,
        int taskId)
    {
        var response = await agentClient.PostAsync(
            $"/api/tasks/claim?serverId={serverId}&agentSessionId={AgentSessionId}",
            null,
            cancellationToken: TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var tasks = await response.Content.ReadFromJsonAsync<List<PendingTaskDto>>(
            TestJsonOptions.Default,
            cancellationToken: TestContext.Current.CancellationToken);
        return Assert.Single(tasks!, task => task.Id == taskId);
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
        {
            existing.AgentProtocolVersion = AgentProtocol.CurrentVersion;
            existing.AgentCapabilitiesJson = "[\"shell.execute\"]";
            await db.SaveChangesAsync();
            return existing.Id;
        }

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
            AgentProtocolVersion = AgentProtocol.CurrentVersion,
            AgentCapabilitiesJson = "[\"shell.execute\"]",
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

    private async Task SetAgentVersionAsync(int serverId, string version)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var server = await db.Servers.FindAsync(
            [serverId],
            TestContext.Current.CancellationToken);
        Assert.NotNull(server);
        server.AgentVersion = version;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
