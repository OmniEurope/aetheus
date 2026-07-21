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

public class LogsControllerIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly string _agentToken = $"test-agent-token-{Guid.NewGuid():N}";
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public LogsControllerIntegrationTests(CustomWebApplicationFactory factory)
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
            new AuthenticationHeaderValue("Bearer", _agentToken);
        return client;
    }

    // ──── GetTaskLogs ────

    [Fact]
    public async Task GetTaskLogs_ExistingTask_ReturnsLogs()
    {
        var (_, taskId) = await SeedServerAndTaskAsync();
        await SeedLogsAsync(taskId);

        var response = await _client.GetAsync($"/api/logs/task/{taskId}", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var logs = await response.Content.ReadFromJsonAsync<List<TaskLogDto>>(TestJsonOptions.Default, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(logs);
        Assert.NotEmpty(logs);
    }

    [Fact]
    public async Task GetTaskLogs_NoLogs_ReturnsEmptyList()
    {
        var (_, taskId) = await SeedServerAndTaskAsync();

        var logs = await _client.GetFromJsonAsync<List<TaskLogDto>>($"/api/logs/task/{taskId}", TestJsonOptions.Default, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(logs);
        Assert.Empty(logs);
    }

    // ──── AppendLog (Agent) ────

    [Fact]
    public async Task AppendLog_ValidRequest_Returns200()
    {
        var (_, taskId) = await SeedServerAndTaskAsync();
        var agentClient = CreateAgentClient();

        var response = await agentClient.PostAsJsonAsync("/api/logs", new AppendLogRequest
        {
            TaskId = taskId,
            Level = TaskLogLevel.Info,
            Message = "Build started"
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AppendLog_ThenGetLogs_ReturnsAppended()
    {
        var (_, taskId) = await SeedServerAndTaskAsync();
        var agentClient = CreateAgentClient();

        await agentClient.PostAsJsonAsync("/api/logs", new AppendLogRequest
        {
            TaskId = taskId,
            Level = TaskLogLevel.Info,
            Message = "Step 1 complete"
        }, cancellationToken: TestContext.Current.CancellationToken);

        var logs = await _client.GetFromJsonAsync<List<TaskLogDto>>($"/api/logs/task/{taskId}", TestJsonOptions.Default, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(logs);
        Assert.Contains(logs, l => l.Message == "Step 1 complete");
    }

    // ──── AppendLogs Batch (Agent) ────

    [Fact]
    public async Task AppendLogs_BatchRequest_Returns200()
    {
        var (_, taskId) = await SeedServerAndTaskAsync();
        var agentClient = CreateAgentClient();

        var requests = new List<AppendLogRequest>
        {
            new() { TaskId = taskId, Level = TaskLogLevel.Info, Message = "Line 1" },
            new() { TaskId = taskId, Level = TaskLogLevel.Warning, Message = "Line 2" },
            new() { TaskId = taskId, Level = TaskLogLevel.Error, Message = "Line 3" }
        };

        var response = await agentClient.PostAsJsonAsync("/api/logs/batch", requests, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AppendLogs_Batch_ThenGetLogs_ReturnsAll()
    {
        var (_, taskId) = await SeedServerAndTaskAsync();
        var agentClient = CreateAgentClient();

        var requests = new List<AppendLogRequest>
        {
            new() { TaskId = taskId, Level = TaskLogLevel.Info, Message = "Batch A" },
            new() { TaskId = taskId, Level = TaskLogLevel.Debug, Message = "Batch B" }
        };
        await agentClient.PostAsJsonAsync("/api/logs/batch", requests, cancellationToken: TestContext.Current.CancellationToken);

        var logs = await _client.GetFromJsonAsync<List<TaskLogDto>>($"/api/logs/task/{taskId}", TestJsonOptions.Default, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(logs);
        Assert.True(logs.Count >= 2);
    }

    // ──── Auth ────

    [Fact]
    public async Task Unauthorized_NoToken_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/logs/task/1", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ──── Helpers ────

    private async Task<(int serverId, int taskId)> SeedServerAndTaskAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var serverName = $"log-test-server-{Guid.NewGuid():N}";
        var server = new Server
        {
            Name = serverName,
            Type = ServerType.Normal,
            Status = ServerStatus.Online,
            IpAddress = "10.0.0.80",
            AgentVersion = "1.0.0",
            OsDescription = "Linux",
            LastHeartbeat = DateTime.UtcNow
        };
        db.Servers.Add(server);
        await db.SaveChangesAsync();

        var hmacKey = Encoding.UTF8.GetBytes("aetheus-dev-key-minimum-32-bytes!!");
        var tokenHash = Convert.ToBase64String(HMACSHA256.HashData(
            hmacKey, Encoding.UTF8.GetBytes(_agentToken)));
        db.ServerTokens.Add(new ServerToken
        {
            ServerId = server.Id,
            TokenHash = tokenHash,
            ExpiresAt = DateTime.UtcNow.AddDays(30)
        });

        var task = new ServerTask
        {
            ServerId = server.Id,
            Name = "log-test-task",
            Command = "echo test",
            Status = TaskExecutionStatus.Running,
            TimeoutSeconds = 60
        };
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        return (server.Id, task.Id);
    }

    private async Task SeedLogsAsync(int taskId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.TaskLogs.AddRange(
            new TaskLog { TaskId = taskId, Level = TaskLogLevel.Info, Message = "Starting..." },
            new TaskLog { TaskId = taskId, Level = TaskLogLevel.Info, Message = "Done." });
        await db.SaveChangesAsync();
    }
}
