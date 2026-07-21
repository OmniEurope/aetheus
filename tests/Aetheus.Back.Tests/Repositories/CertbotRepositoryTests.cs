// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Certbot;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.Tests.Repositories;

public class CertbotRepositoryTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly CertbotRepository _repo;
    private readonly int _serverId;

    public CertbotRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _repo = new CertbotRepository(_db);

        var server = new Server { Name = "S1", Hostname = "h1", Status = ServerStatus.Online };
        _db.Servers.Add(server);
        _db.SaveChanges();
        _serverId = server.Id;
    }

    [Fact]
    public async Task GetCertificatesAsync_ReturnsOrderedByName()
    {
        _db.CertbotCertificates.AddRange(
            new CertbotCertificate { ServerId = _serverId, Name = "zeta.com", Domains = "[\"zeta.com\"]", ExpiryDate = DateTime.UtcNow.AddDays(90) },
            new CertbotCertificate { ServerId = _serverId, Name = "alpha.com", Domains = "[\"alpha.com\"]", ExpiryDate = DateTime.UtcNow.AddDays(90) }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetCertificatesAsync(_serverId, ct: TestContext.Current.CancellationToken);
        Assert.Equal(2, result.Count);
        Assert.Equal("alpha.com", result[0].Name);
    }

    [Fact]
    public async Task ServerExistsAsync_Exists_ReturnsTrue()
    {
        var result = await _repo.ServerExistsAsync(_serverId, ct: TestContext.Current.CancellationToken);
        Assert.True(result);
    }

    [Fact]
    public async Task ServerExistsAsync_NotExists_ReturnsFalse()
    {
        var result = await _repo.ServerExistsAsync(999, ct: TestContext.Current.CancellationToken);
        Assert.False(result);
    }

    [Fact]
    public async Task AddTaskAsync_Persists()
    {
        await _repo.AddTaskAsync(new ServerTask { ServerId = _serverId, Name = "certbot_action", Command = "renew", Status = TaskExecutionStatus.Pending }, ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, await _db.Tasks.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    public void Dispose() => _db.Dispose();
}
