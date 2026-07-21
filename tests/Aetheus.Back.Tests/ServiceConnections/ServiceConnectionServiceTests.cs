// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Audit;
using Aetheus.Back.Components.ServiceConnections;
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using NSubstitute;

namespace Aetheus.Back.Tests;

public class ServiceConnectionServiceTests
{
    private readonly IServiceConnectionRepository _repo = Substitute.For<IServiceConnectionRepository>();
    private readonly IEncryptionService _encryption = Substitute.For<IEncryptionService>();
    private readonly IAuditService _audit = Substitute.For<IAuditService>();
    private readonly IServiceConnectionTester _tester = Substitute.For<IServiceConnectionTester>();
    private readonly ServiceConnectionService _sut;

    public ServiceConnectionServiceTests()
    {
        _sut = new ServiceConnectionService(_repo, _encryption, _audit, Substitute.For<IEntityChangeNotifier>(), _tester, TimeProvider.System);
    }

    [Fact]
    public async Task GetConnectionsAsync_ReturnsPaginatedResult()
    {
        var conn = new ServiceConnection { Id = 1, Name = "github", Type = ServiceConnectionType.GitHub };
        _repo.GetPagedAsync(null, null, 1, 10, Arg.Any<List<int>?>(), Arg.Any<CancellationToken>())
            .Returns((new List<ServiceConnection> { conn }, 1));

        var result = await _sut.GetConnectionsAsync(null, new PaginationRequest { Page = 1, PageSize = 10 }, ct: TestContext.Current.CancellationToken);

        Assert.Single(result.Items);
        Assert.Equal("github", result.Items[0].Name);
    }

    [Fact]
    public async Task GetConnectionAsync_Found_DecryptsPayload()
    {
        _repo.GetDetailAsync(1, Arg.Any<CancellationToken>())
            .Returns(new ServiceConnection
            {
                Id = 1,
                Name = "azure",
                Type = ServiceConnectionType.Generic,
                EncryptedPayload = "encrypted-data"
            });
        _encryption.DecryptValue("encrypted-data").Returns("{\"pat\":\"secret\"}");

        var result = await _sut.GetConnectionAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("{\"pat\":\"secret\"}", result.ConfigurationJson);
    }

    [Fact]
    public async Task GetConnectionAsync_NotFound_ReturnsNull()
    {
        _repo.GetDetailAsync(99, Arg.Any<CancellationToken>())
            .Returns((ServiceConnection?)null);

        Assert.Null(await _sut.GetConnectionAsync(99, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CreateConnectionAsync_EncryptsPayloadAndCreates()
    {
        _encryption.EncryptValue("{\"token\":\"abc\"}").Returns("encrypted-abc");
        _repo.AddAsync(Arg.Any<ServiceConnection>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _repo.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var result = await _sut.CreateConnectionAsync(new CreateServiceConnectionRequest
        {
            Name = "gitlab",
            Type = ServiceConnectionType.GitLab,
            ConfigurationJson = "{\"token\":\"abc\"}"
        }, ct: TestContext.Current.CancellationToken);

        Assert.Equal("gitlab", result.Name);
        await _repo.Received(1).AddAsync(
            Arg.Is<ServiceConnection>(c => c.EncryptedPayload == "encrypted-abc"),
            Arg.Any<CancellationToken>());
        await _audit.Received(1).LogAsync("Created", "ServiceConnection", Arg.Any<int>(), "gitlab", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateConnectionAsync_NotFound_ReturnsNull()
    {
        _repo.FindAsync(99, Arg.Any<CancellationToken>())
            .Returns((ServiceConnection?)null);

        Assert.Null(await _sut.UpdateConnectionAsync(99, new UpdateServiceConnectionRequest
        {
            Name = "x",
            ConfigurationJson = "{}"
        }, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UpdateConnectionAsync_Found_ReEncryptsAndUpdates()
    {
        _repo.FindAsync(1, Arg.Any<CancellationToken>())
            .Returns(new ServiceConnection { Id = 1, Name = "old", EncryptedPayload = "old-enc" });
        _encryption.EncryptValue("{\"new\":true}").Returns("new-enc");
        _repo.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var result = await _sut.UpdateConnectionAsync(1, new UpdateServiceConnectionRequest
        {
            Name = "updated",
            ConfigurationJson = "{\"new\":true}"
        }, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("updated", result.Name);
        await _audit.Received(1).LogAsync("Updated", "ServiceConnection", 1, "updated", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteConnectionAsync_NotFound_ReturnsFalse()
    {
        _repo.FindAsync(99, Arg.Any<CancellationToken>())
            .Returns((ServiceConnection?)null);

        Assert.False(await _sut.DeleteConnectionAsync(99, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteConnectionAsync_Found_DeletesAndReturnsTrue()
    {
        _repo.FindAsync(1, Arg.Any<CancellationToken>())
            .Returns(new ServiceConnection { Id = 1, Name = "conn" });
        _repo.RemoveAsync(Arg.Any<ServiceConnection>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        Assert.True(await _sut.DeleteConnectionAsync(1, ct: TestContext.Current.CancellationToken));
        await _audit.Received(1).LogAsync("Deleted", "ServiceConnection", 1, "conn", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveConnectionSecretsAsync_ReturnsDecryptedSecrets()
    {
        _repo.FindByNamesAsync(Arg.Any<List<string>>(), null, Arg.Any<CancellationToken>())
            .Returns([new ServiceConnection
            {
                Id = 1, Name = "my-conn", Type = ServiceConnectionType.GitHub,
                Url = "https://github.com", EncryptedPayload = "enc"
            }]);
        _encryption.DecryptValue("enc").Returns("{\"pat\":\"tok\"}");

        var result = await _sut.ResolveConnectionSecretsAsync(["my-conn"], null, ct: TestContext.Current.CancellationToken);

        Assert.True(result.ContainsKey("SVC_MY-CONN_URL"));
        Assert.Equal("https://github.com", result["SVC_MY-CONN_URL"]);
        Assert.True(result.ContainsKey("SVC_MY-CONN_CONFIG"));
        Assert.Equal("{\"pat\":\"tok\"}", result["SVC_MY-CONN_CONFIG"]);
    }

    [Fact]
    public async Task TestConnectionAsync_NotFound_ReturnsNull()
    {
        _repo.FindAsync(99, Arg.Any<CancellationToken>()).Returns((ServiceConnection?)null);

        Assert.Null(await _sut.TestConnectionAsync(99, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TestConnectionAsync_Found_DecryptsAndDelegatesToTester()
    {
        _repo.FindAsync(1, Arg.Any<CancellationToken>())
            .Returns(new ServiceConnection { Id = 1, Type = ServiceConnectionType.GitHub, Url = "https://github.com", EncryptedPayload = "enc" });
        _encryption.DecryptValue("enc").Returns("{\"token\":\"t\"}");
        _tester.TestAsync(ServiceConnectionType.GitHub, "https://github.com", "{\"token\":\"t\"}", Arg.Any<CancellationToken>())
            .Returns(new ServiceConnectionTestResultDto { Status = ServiceConnectionTestStatus.Valid });

        var result = await _sut.TestConnectionAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(ServiceConnectionTestStatus.Valid, result!.Status);
        await _tester.Received(1).TestAsync(ServiceConnectionType.GitHub, "https://github.com", "{\"token\":\"t\"}", Arg.Any<CancellationToken>());
    }
}
