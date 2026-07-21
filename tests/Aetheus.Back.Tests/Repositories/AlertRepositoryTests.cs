// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Alerts;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.Tests.Repositories;

public class AlertRepositoryTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly AlertRepository _repo;
    private readonly int _serverId;

    public AlertRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _repo = new AlertRepository(_db, TimeProvider.System);

        var server = new Server { Name = "S1", Hostname = "h1", Status = ServerStatus.Online };
        _db.Servers.Add(server);
        _db.SaveChanges();
        _serverId = server.Id;
    }

    [Fact]
    public async Task GetAllAsync_ReturnsOrderedByName()
    {
        _db.AlertRules.AddRange(
            new AlertRule { Name = "Zeta", ServerId = _serverId, Metric = MetricType.Cpu, Operator = ComparisonOperator.GreaterThan, Threshold = 80 },
            new AlertRule { Name = "Alpha", ServerId = _serverId, Metric = MetricType.Memory, Operator = ComparisonOperator.GreaterThan, Threshold = 90 }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetAllAsync(ct: TestContext.Current.CancellationToken);
        Assert.Equal(2, result.Count);
        Assert.Equal("Alpha", result[0].Name);
    }

    [Fact]
    public async Task GetEnabledAsync_ReturnsOnlyEnabled()
    {
        _db.AlertRules.AddRange(
            new AlertRule { Name = "Enabled", ServerId = _serverId, Metric = MetricType.Cpu, Operator = ComparisonOperator.GreaterThan, Threshold = 80, IsEnabled = true },
            new AlertRule { Name = "Disabled", ServerId = _serverId, Metric = MetricType.Memory, Operator = ComparisonOperator.GreaterThan, Threshold = 90, IsEnabled = false }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetEnabledAsync(ct: TestContext.Current.CancellationToken);
        Assert.Single(result);
        Assert.Equal("Enabled", result[0].Name);
    }

    [Fact]
    public async Task GetByIdAsync_Found_IncludesServerAndChannel()
    {
        var channel = new NotificationChannel { Name = "Ch", Type = NotificationChannelType.Email };
        _db.NotificationChannels.Add(channel);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var rule = new AlertRule { Name = "R1", ServerId = _serverId, NotificationChannelId = channel.Id, Metric = MetricType.Cpu, Operator = ComparisonOperator.GreaterThan, Threshold = 80 };
        _db.AlertRules.Add(rule);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetByIdAsync(rule.Id, ct: TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.NotNull(result.Server);
        Assert.NotNull(result.NotificationChannel);
    }

    [Fact]
    public async Task GetByIdAsync_NotFound_ReturnsNull()
    {
        var result = await _repo.GetByIdAsync(999, ct: TestContext.Current.CancellationToken);
        Assert.Null(result);
    }

    [Fact]
    public async Task FindAsync_Found_IncludesServer()
    {
        var rule = new AlertRule { Name = "R1", ServerId = _serverId, Metric = MetricType.Cpu, Operator = ComparisonOperator.GreaterThan, Threshold = 80 };
        _db.AlertRules.Add(rule);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.FindAsync(rule.Id, ct: TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.NotNull(result.Server);
    }

    [Fact]
    public async Task FindAsync_NotFound_ReturnsNull()
    {
        var result = await _repo.FindAsync(999, ct: TestContext.Current.CancellationToken);
        Assert.Null(result);
    }

    [Fact]
    public async Task AddAsync_Persists()
    {
        await _repo.AddAsync(new AlertRule { Name = "New", ServerId = _serverId, Metric = MetricType.Cpu, Operator = ComparisonOperator.GreaterThan, Threshold = 80 }, ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, await _db.AlertRules.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RemoveAsync_Removes()
    {
        var rule = new AlertRule { Name = "Del", ServerId = _serverId, Metric = MetricType.Cpu, Operator = ComparisonOperator.GreaterThan, Threshold = 80 };
        _db.AlertRules.Add(rule);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        await _repo.RemoveAsync(rule, ct: TestContext.Current.CancellationToken);
        Assert.Equal(0, await _db.AlertRules.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetRecentMetricsAsync_ReturnsOnlyRecentOrderedByTimestampDesc()
    {
        _db.ServerMetrics.AddRange(
            new ServerMetric { ServerId = _serverId, CpuPercent = 10, MemoryUsedMb = 100, MemoryTotalMb = 1000, DiskUsedGb = 1, DiskTotalGb = 10, Timestamp = DateTime.UtcNow.AddSeconds(-30) },
            new ServerMetric { ServerId = _serverId, CpuPercent = 20, MemoryUsedMb = 200, MemoryTotalMb = 1000, DiskUsedGb = 2, DiskTotalGb = 10, Timestamp = DateTime.UtcNow.AddSeconds(-10) },
            new ServerMetric { ServerId = _serverId, CpuPercent = 50, MemoryUsedMb = 500, MemoryTotalMb = 1000, DiskUsedGb = 5, DiskTotalGb = 10, Timestamp = DateTime.UtcNow.AddSeconds(-120) }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetRecentMetricsAsync(_serverId, 60, ct: TestContext.Current.CancellationToken);
        Assert.Equal(2, result.Count);
        Assert.Equal(20, result[0].CpuPercent);
    }

    [Fact]
    public async Task SaveChangesAsync_PersistsPendingChanges()
    {
        _db.AlertRules.Add(new AlertRule { Name = "P", ServerId = _serverId, Metric = MetricType.Cpu, Operator = ComparisonOperator.GreaterThan, Threshold = 80 });
        await _repo.SaveChangesAsync(ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, await _db.AlertRules.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    public void Dispose() => _db.Dispose();
}
