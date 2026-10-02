// SPDX-License-Identifier: EUPL-1.2
using System.Text.Json;
using Aetheus.Back.Components.Audit;
using Aetheus.Back.Components.Auth;
using Aetheus.Back.Components.Shared;
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Exceptions;
using Aetheus.Back.Services;
using NSubstitute;
using OtpNet;

namespace Aetheus.Back.Tests;

public class TotpServiceTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);
    private readonly IAuthRepository _repoMock = Substitute.For<IAuthRepository>();
    private readonly IEncryptionService _encryptionMock = Substitute.For<IEncryptionService>();
    private readonly IAuditService _auditMock = Substitute.For<IAuditService>();
    private readonly TimeProvider _timeProvider = Substitute.For<TimeProvider>();
    private readonly TotpService _sut;

    public TotpServiceTests()
    {
        _timeProvider.GetUtcNow().Returns(FixedNow);

        // Encryption round-trips: encrypt returns "enc:" prefix, decrypt strips it.
        _encryptionMock.EncryptValue(Arg.Any<string>()).Returns(ci => $"enc:{ci.Arg<string>()}");
        _encryptionMock.DecryptValue(Arg.Any<string>()).Returns(ci => ci.Arg<string>().Replace("enc:", ""));

        var jwtOptions = new JwtOptions { SigningKey = "aetheus-dev-key-minimum-32-bytes!!" };
        _sut = new TotpService(_repoMock, _encryptionMock, _auditMock, jwtOptions, _timeProvider);
    }

    // --- SetupTotpAsync ---

    [Fact]
    public async Task SetupTotpAsync_UserNotFound_ThrowsNotFoundException()
    {
        _repoMock.FindUserByIdForUpdateAsync(99, Arg.Any<CancellationToken>())
            .Returns((User?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.SetupTotpAsync(99, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SetupTotpAsync_TotpAlreadyEnabled_ThrowsConflictException()
    {
        _repoMock.FindUserByIdForUpdateAsync(1, Arg.Any<CancellationToken>())
            .Returns(new User { Id = 1, Username = "admin", TotpEnabled = true });

        await Assert.ThrowsAsync<ConflictException>(() => _sut.SetupTotpAsync(1, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SetupTotpAsync_ValidUser_ReturnsSetupResponse()
    {
        var user = new User { Id = 1, Username = "admin", TotpEnabled = false };
        _repoMock.FindUserByIdForUpdateAsync(1, Arg.Any<CancellationToken>()).Returns(user);

        var result = await _sut.SetupTotpAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.False(string.IsNullOrEmpty(result.SharedKey));
        Assert.Contains("otpauth://totp/", result.AuthenticatorUri);
        Assert.Contains("admin", result.AuthenticatorUri);
        Assert.Equal(8, result.RecoveryCodes.Count);
        Assert.All(result.RecoveryCodes, code => Assert.Contains("-", code));
        await _repoMock.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetupTotpAsync_SetsEncryptedSecretOnUser()
    {
        var user = new User { Id = 1, Username = "admin", TotpEnabled = false };
        _repoMock.FindUserByIdForUpdateAsync(1, Arg.Any<CancellationToken>()).Returns(user);

        await _sut.SetupTotpAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(user.TotpSecret);
        Assert.StartsWith("enc:", user.TotpSecret);
        Assert.NotNull(user.TotpRecoveryCodes);
    }

    // --- VerifyAndEnableTotpAsync ---

    [Fact]
    public async Task VerifyAndEnableTotpAsync_UserNotFound_ReturnsFalse()
    {
        _repoMock.FindUserByIdForUpdateAsync(99, Arg.Any<CancellationToken>())
            .Returns((User?)null);

        var result = await _sut.VerifyAndEnableTotpAsync(99, "123456", ct: TestContext.Current.CancellationToken);

        Assert.False(result);
    }

    [Fact]
    public async Task VerifyAndEnableTotpAsync_NoSecret_ReturnsFalse()
    {
        _repoMock.FindUserByIdForUpdateAsync(1, Arg.Any<CancellationToken>())
            .Returns(new User { Id = 1, TotpSecret = null });

        var result = await _sut.VerifyAndEnableTotpAsync(1, "123456", ct: TestContext.Current.CancellationToken);

        Assert.False(result);
    }

    [Fact]
    public async Task VerifyAndEnableTotpAsync_AlreadyEnabled_ReturnsTrue()
    {
        _repoMock.FindUserByIdForUpdateAsync(1, Arg.Any<CancellationToken>())
            .Returns(new User { Id = 1, TotpSecret = "enc:JBSWY3DPEHPK3PXP", TotpEnabled = true });

        var result = await _sut.VerifyAndEnableTotpAsync(1, "123456", ct: TestContext.Current.CancellationToken);

        Assert.True(result);
    }

    [Fact]
    public async Task VerifyAndEnableTotpAsync_ValidCode_EnablesAndReturnsTrue()
    {
        // Generate a real TOTP code to validate
        var secret = KeyGeneration.GenerateRandomKey(20);
        var base32Secret = Base32Encoding.ToString(secret);
        var totp = new Totp(secret, step: 30, totpSize: 6);
        var code = totp.ComputeTotp(FixedNow.UtcDateTime);

        var user = new User { Id = 1, Username = "admin", TotpSecret = $"enc:{base32Secret}", TotpEnabled = false };
        _repoMock.FindUserByIdForUpdateAsync(1, Arg.Any<CancellationToken>()).Returns(user);

        var result = await _sut.VerifyAndEnableTotpAsync(1, code, ct: TestContext.Current.CancellationToken);

        Assert.True(result);
        Assert.True(user.TotpEnabled);
        await _auditMock.Received(1).LogAsync("TotpEnabled", "User", 1, "admin", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VerifyAndEnableTotpAsync_InvalidCode_ReturnsFalse()
    {
        var secret = KeyGeneration.GenerateRandomKey(20);
        var base32Secret = Base32Encoding.ToString(secret);

        var user = new User { Id = 1, Username = "admin", TotpSecret = $"enc:{base32Secret}", TotpEnabled = false };
        _repoMock.FindUserByIdForUpdateAsync(1, Arg.Any<CancellationToken>()).Returns(user);

        var result = await _sut.VerifyAndEnableTotpAsync(1, "000000", ct: TestContext.Current.CancellationToken);

        Assert.False(result);
        Assert.False(user.TotpEnabled);
    }

    // --- DisableTotpAsync ---

    [Fact]
    public async Task DisableTotpAsync_UserNotFound_ReturnsFalse()
    {
        _repoMock.FindUserByIdForUpdateAsync(99, Arg.Any<CancellationToken>())
            .Returns((User?)null);

        var result = await _sut.DisableTotpAsync(99, "password", ct: TestContext.Current.CancellationToken);

        Assert.False(result);
    }

    [Fact]
    public async Task DisableTotpAsync_TotpNotEnabled_ReturnsFalse()
    {
        _repoMock.FindUserByIdForUpdateAsync(1, Arg.Any<CancellationToken>())
            .Returns(new User { Id = 1, TotpEnabled = false });

        var result = await _sut.DisableTotpAsync(1, "password", ct: TestContext.Current.CancellationToken);

        Assert.False(result);
    }

    [Fact]
    public async Task DisableTotpAsync_WrongPassword_ReturnsFalse()
    {
        var hash = BCrypt.Net.BCrypt.HashPassword("correctpassword");
        _repoMock.FindUserByIdForUpdateAsync(1, Arg.Any<CancellationToken>())
            .Returns(new User { Id = 1, Username = "admin", TotpEnabled = true, PasswordHash = hash });

        var result = await _sut.DisableTotpAsync(1, "wrongpassword", ct: TestContext.Current.CancellationToken);

        Assert.False(result);
    }

    [Fact]
    public async Task DisableTotpAsync_CorrectPassword_ClearsAndReturnsTrue()
    {
        var hash = BCrypt.Net.BCrypt.HashPassword("mypassword");
        var user = new User
        {
            Id = 1,
            Username = "admin",
            TotpEnabled = true,
            TotpSecret = "enc:secret",
            TotpRecoveryCodes = "[\"hash1\"]",
            PasswordHash = hash
        };
        _repoMock.FindUserByIdForUpdateAsync(1, Arg.Any<CancellationToken>()).Returns(user);

        var result = await _sut.DisableTotpAsync(1, "mypassword", ct: TestContext.Current.CancellationToken);

        Assert.True(result);
        Assert.Null(user.TotpSecret);
        Assert.False(user.TotpEnabled);
        Assert.Null(user.TotpRecoveryCodes);
        await _auditMock.Received(1).LogAsync("TotpDisabled", "User", 1, "admin", Arg.Any<CancellationToken>());
    }

    // --- ConsumeRecoveryCodeAsync ---

    [Fact]
    public async Task ConsumeRecoveryCodeAsync_UserNotFound_ReturnsFalse()
    {
        _repoMock.FindUserByIdForUpdateAsync(99, Arg.Any<CancellationToken>())
            .Returns((User?)null);

        var result = await _sut.ConsumeRecoveryCodeAsync(99, "ABCDE-12345", TestContext.Current.CancellationToken);

        Assert.False(result);
    }

    [Fact]
    public async Task ConsumeRecoveryCodeAsync_ValidCode_RemovesAndReturnsTrue()
    {
        var plainCode = "ABCDE12345";
        var hashedCode = BCrypt.Net.BCrypt.HashPassword(plainCode);
        var otherHash = BCrypt.Net.BCrypt.HashPassword("OTHER67890");
        var hashes = JsonSerializer.Serialize(new List<string> { hashedCode, otherHash });

        var user = new User
        {
            Id = 1,
            Username = "admin",
            TotpRecoveryCodes = hashes
        };
        _repoMock.FindUserByIdForUpdateAsync(1, Arg.Any<CancellationToken>()).Returns(user);

        var result = await _sut.ConsumeRecoveryCodeAsync(1, "ABCDE-12345", TestContext.Current.CancellationToken);

        Assert.True(result);
        // One code should have been removed
        var remaining = JsonSerializer.Deserialize<List<string>>(user.TotpRecoveryCodes!);
        Assert.Single(remaining!);
        await _auditMock.Received(1).LogAsync("RecoveryCodeUsed", "User", 1, "admin", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConsumeRecoveryCodeAsync_InvalidCode_ReturnsFalse()
    {
        var hashedCode = BCrypt.Net.BCrypt.HashPassword("VALIDCODE1");
        var hashes = JsonSerializer.Serialize(new List<string> { hashedCode });

        var user = new User
        {
            Id = 1,
            Username = "admin",
            TotpRecoveryCodes = hashes
        };
        _repoMock.FindUserByIdForUpdateAsync(1, Arg.Any<CancellationToken>()).Returns(user);

        var result = await _sut.ConsumeRecoveryCodeAsync(1, "WRONG-CODE0", TestContext.Current.CancellationToken);

        Assert.False(result);
    }

    // --- ValidateTotpCode ---

    [Fact]
    public void ValidateTotpCode_ValidCode_ReturnsTrue()
    {
        var secret = KeyGeneration.GenerateRandomKey(20);
        var base32Secret = Base32Encoding.ToString(secret);
        var totp = new Totp(secret, step: 30, totpSize: 6);
        var code = totp.ComputeTotp(FixedNow.UtcDateTime);

        var result = _sut.ValidateTotpCode($"enc:{base32Secret}", code);

        Assert.True(result);
    }

    [Fact]
    public void ValidateTotpCode_CodeOutsideAcceptedWindow_ReturnsFalse()
    {
        var secret = KeyGeneration.GenerateRandomKey(20);
        var base32Secret = Base32Encoding.ToString(secret);
        var totp = new Totp(secret, step: 30, totpSize: 6);
        var expiredCode = totp.ComputeTotp(FixedNow.AddMinutes(-2).UtcDateTime);

        var result = _sut.ValidateTotpCode($"enc:{base32Secret}", expiredCode);

        Assert.False(result);
    }

    [Fact]
    public void ValidateTotpCode_InvalidCode_ReturnsFalse()
    {
        var secret = KeyGeneration.GenerateRandomKey(20);
        var base32Secret = Base32Encoding.ToString(secret);

        var result = _sut.ValidateTotpCode($"enc:{base32Secret}", "000000");

        Assert.False(result);
    }
}
