// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Audit;
using Aetheus.Back.Components.Auth;
using Aetheus.Back.Components.Organizations;
using Aetheus.Back.Data.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Aetheus.Back.Tests.Auth;

public class AuthServiceRefreshTokenTests
{
    private readonly IAuthRepository _repo = Substitute.For<IAuthRepository>();
    private readonly AuthService _sut;

    public AuthServiceRefreshTokenTests()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:JwtKey"] = "aetheus-dev-key-minimum-32-bytes!!"
            })
            .Build();

        _sut = new AuthService(
            _repo, config, Substitute.For<IAuditService>(),
            new JwtOptions { SigningKey = "aetheus-dev-key-minimum-32-bytes!!" },
            Substitute.For<IMemoryCache>(), Substitute.For<IOrganizationService>(),
            Substitute.For<ITotpService>(), TimeProvider.System,
            Substitute.For<IHttpContextAccessor>(), Substitute.For<Aetheus.Back.Services.IAdminChangeNotifier>(), Substitute.For<ILogger<AuthService>>());
    }

    private static User ActiveUser(int id = 1) =>
        new() { Id = id, Username = "bob", IsActive = true, UserRoles = [] };

    private void Stored(RefreshToken token) =>
        _repo.FindRefreshTokenByHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(token);

    [Fact]
    public async Task RefreshToken_NotFound_ReturnsNull()
    {
        _repo.FindRefreshTokenByHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((RefreshToken?)null);

        Assert.Null(await _sut.RefreshTokenAsync("plaintext", ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RefreshToken_Valid_RotatesAndReturnsNewRefreshToken()
    {
        Stored(new RefreshToken
        {
            Id = 10,
            UserId = 1,
            ExpiresAt = DateTime.UtcNow.AddDays(1),
            User = ActiveUser()
        });

        var result = await _sut.RefreshTokenAsync("plaintext", ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.False(string.IsNullOrEmpty(result.Token));
        Assert.False(string.IsNullOrEmpty(result.RefreshToken));
        Assert.NotEqual("plaintext", result.RefreshToken);
        await _repo.Received(1).AddRefreshTokenAsync(Arg.Any<RefreshToken>(), Arg.Any<CancellationToken>());
        await _repo.Received(1).RevokeRefreshTokenAsync(10, Arg.Any<int?>(), Arg.Any<CancellationToken>());
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefreshToken_Expired_ReturnsNull()
    {
        Stored(new RefreshToken
        {
            Id = 11,
            UserId = 1,
            ExpiresAt = DateTime.UtcNow.AddDays(-1),
            User = ActiveUser()
        });

        Assert.Null(await _sut.RefreshTokenAsync("plaintext", ct: TestContext.Current.CancellationToken));
        await _repo.DidNotReceive().AddRefreshTokenAsync(Arg.Any<RefreshToken>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefreshToken_InactiveUser_ReturnsNull()
    {
        Stored(new RefreshToken
        {
            Id = 12,
            UserId = 1,
            ExpiresAt = DateTime.UtcNow.AddDays(1),
            User = new User { Id = 1, Username = "bob", IsActive = false, UserRoles = [] }
        });

        Assert.Null(await _sut.RefreshTokenAsync("plaintext", ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RefreshToken_RevokedOutsideGrace_RevokesAllAndReturnsNull()
    {
        Stored(new RefreshToken
        {
            Id = 13,
            UserId = 5,
            ExpiresAt = DateTime.UtcNow.AddDays(1),
            RevokedAt = DateTime.UtcNow.AddHours(-1), // well outside the grace window
            User = ActiveUser(5)
        });

        var result = await _sut.RefreshTokenAsync("plaintext", ct: TestContext.Current.CancellationToken);

        Assert.Null(result);
        await _repo.Received(1).RevokeAllUserRefreshTokensAsync(5, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefreshToken_RevokedWithinGraceWithReplacement_ReissuesAccessTokenOnly()
    {
        Stored(new RefreshToken
        {
            Id = 14,
            UserId = 1,
            ExpiresAt = DateTime.UtcNow.AddDays(1),
            RevokedAt = DateTime.UtcNow, // just revoked → inside grace
            ReplacedById = 99,
            User = ActiveUser()
        });

        var result = await _sut.RefreshTokenAsync("plaintext", ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.False(string.IsNullOrEmpty(result.Token));
        // Grace re-issue does NOT mint a new refresh token.
        await _repo.DidNotReceive().AddRefreshTokenAsync(Arg.Any<RefreshToken>(), Arg.Any<CancellationToken>());
    }
}
