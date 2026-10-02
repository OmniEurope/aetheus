// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Audit;
using Aetheus.Back.Components.Vaults;
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Exceptions;
using Aetheus.Back.Services;
using Microsoft.Extensions.Caching.Memory;
using NSubstitute;

namespace Aetheus.Back.Tests;

public class VaultServiceRotateTests
{
    private readonly IVaultRepository _repo = Substitute.For<IVaultRepository>();
    private readonly IEncryptionService _encryption = Substitute.For<IEncryptionService>();
    private readonly IDbTransactionScope _transaction = Substitute.For<IDbTransactionScope>();
    private readonly IAuditService _audit = Substitute.For<IAuditService>();
    private readonly VaultService _sut;

    public VaultServiceRotateTests()
    {
        // ExecuteInTransactionAsync is an extension over Begin/Commit/Rollback; a substitute scope
        // satisfies those by default and runs the transactional body for real - no extra setup needed.
        _sut = new VaultService(_repo, _encryption, _transaction, _audit,
            Substitute.For<IEntityChangeNotifier>(), TimeProvider.System, Substitute.For<IMemoryCache>());
    }

    private static VaultSecret Secret(int id = 1, int vaultId = 1) =>
        new() { Id = id, VaultId = vaultId, Key = "API_KEY", EncryptedValue = "enc-old" };

    [Fact]
    public async Task Rotate_SecretNotFound_ReturnsNull()
    {
        _repo.FindSecretAsync(9, Arg.Any<CancellationToken>()).Returns((VaultSecret?)null);

        Assert.Null(await _sut.RotateSecretAsync(1, 9, new RotateVaultSecretRequest { Value = "x" }, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Rotate_VaultMismatch_ReturnsNull()
    {
        _repo.FindSecretAsync(1, Arg.Any<CancellationToken>()).Returns(Secret(vaultId: 2));

        Assert.Null(await _sut.RotateSecretAsync(1, 1, new RotateVaultSecretRequest { Value = "x" }, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Rotate_SameValue_ThrowsBadRequest()
    {
        _repo.FindSecretAsync(1, Arg.Any<CancellationToken>()).Returns(Secret());
        _encryption.DecryptValue("enc-old").Returns("same-value");

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.RotateSecretAsync(1, 1, new RotateVaultSecretRequest { Value = "same-value" }, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Rotate_NewValue_EncryptsVersionsAndAudits()
    {
        var secret = Secret();
        _repo.FindSecretAsync(1, Arg.Any<CancellationToken>()).Returns(secret);
        _encryption.DecryptValue("enc-old").Returns("old-value");
        _encryption.EncryptValue("new-value").Returns("enc-new");
        _repo.GetNextVersionAsync(1, Arg.Any<CancellationToken>()).Returns(3);

        var result = await _sut.RotateSecretAsync(1, 1, new RotateVaultSecretRequest { Value = "new-value" }, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("enc-new", secret.EncryptedValue);
        await _repo.Received(1).AddSecretVersionAsync(
            Arg.Is<VaultSecretVersion>(v => v.Version == 3 && v.ChangeType == ChangeType.Rotated), Arg.Any<CancellationToken>());
        await _audit.Received(1).LogAsync("Rotated", "VaultSecret", 1, "API_KEY", Arg.Any<CancellationToken>());
    }
}
