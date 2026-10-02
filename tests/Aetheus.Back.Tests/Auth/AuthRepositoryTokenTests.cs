// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Auth;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Aetheus.Back.Tests.Auth;

/// <summary>
/// The token side of the auth repository: agent enrollment on a non-relational store, the
/// server-token lifecycle (latest active expiry, dead-token sweep, existence probes) and the
/// refresh-token expiry sweeps with their five-minute revocation grace.
///
/// The relational enrollment path is a transaction plus three <c>ExecuteUpdate</c> statements and is
/// proved against PostgreSQL in the integration suite; the branch guard that refuses it on a
/// non-relational provider is asserted here.
/// </summary>
public sealed class AuthRepositoryTokenTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTime NowUtc = Now.UtcDateTime;

    private readonly FakeTimeProvider _clock = new(Now);
    private readonly AppDbContext _db;
    private readonly AuthRepository _repository;

    public AuthRepositoryTokenTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        _db = new AppDbContext(options, _clock);
        _repository = new AuthRepository(_db, _clock);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task SaveAsync() => await _db.SaveChangesAsync(Ct);

    private static ServerToken Token(
        int id, int serverId, string hash, bool revoked = false, DateTime? expiresAt = null) =>
        new()
        {
            Id = id,
            ServerId = serverId,
            TokenHash = hash,
            IsRevoked = revoked,
            ExpiresAt = expiresAt ?? NowUtc.AddDays(30)
        };

    private static RegistrationToken Registration(
        int id, bool used = false, DateTime? expiresAt = null) =>
        new()
        {
            Id = id,
            Token = $"hash-{id}",
            IsUsed = used,
            OrganizationId = 7,
            ExpiresAt = expiresAt ?? NowUtc.AddHours(1)
        };

    private static RefreshToken Refresh(
        int id, int userId, DateTime expiresAt, DateTime? revokedAt = null) =>
        new() { Id = id, UserId = userId, TokenHash = $"hash-{id}", ExpiresAt = expiresAt, RevokedAt = revokedAt };

    // ---------- enrollment (non-relational path) ----------

    [Fact]
    public async Task TryPersistServerEnrollmentAsync_ConsumesTheTokenAndAttachesTheNewServer()
    {
        _db.RegistrationTokens.Add(Registration(1));
        await SaveAsync();
        _db.ChangeTracker.Clear();
        var server = new Server { Name = "runner", Hostname = "runner" };

        var persisted = await _repository.TryPersistServerEnrollmentAsync(
            1, server, new ServerToken { TokenHash = "fresh", ExpiresAt = NowUtc.AddDays(30) }, Ct);

        Assert.True(persisted);
        var registration = await _db.RegistrationTokens.AsNoTracking().FirstAsync(Ct);
        Assert.True(registration.IsUsed);
        Assert.Equal(server.Id, registration.UsedByServerId);
        Assert.Equal("fresh", (await _db.ServerTokens.AsNoTracking().FirstAsync(Ct)).TokenHash);
    }

    [Fact]
    public async Task TryPersistServerEnrollmentAsync_RevokesTheActiveTokensOfAReEnrollingServer()
    {
        _db.RegistrationTokens.Add(Registration(1));
        _db.Servers.Add(new Server { Id = 5, Name = "runner", Hostname = "runner" });
        _db.ServerTokens.AddRange(
            Token(10, 5, "active"),
            Token(11, 5, "already-revoked", revoked: true),
            Token(12, 5, "expired", expiresAt: NowUtc.AddDays(-1)),
            Token(13, 6, "another-server"));
        await SaveAsync();
        _db.ChangeTracker.Clear();
        var server = await _db.Servers.FirstAsync(item => item.Id == 5, Ct);

        var persisted = await _repository.TryPersistServerEnrollmentAsync(
            1, server, new ServerToken { ServerId = 5, TokenHash = "fresh", ExpiresAt = NowUtc.AddDays(30) }, Ct);

        Assert.True(persisted);
        _db.ChangeTracker.Clear();
        var tokens = await _db.ServerTokens.AsNoTracking().ToDictionaryAsync(token => token.TokenHash, Ct);
        Assert.True(tokens["active"].IsRevoked);
        Assert.False(tokens["another-server"].IsRevoked);
        Assert.False(tokens["fresh"].IsRevoked);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task TryPersistServerEnrollmentAsync_RefusesAConsumedOrExpiredRegistrationToken(
        bool used, bool expired)
    {
        _db.RegistrationTokens.Add(
            Registration(1, used, expired ? NowUtc.AddSeconds(-1) : NowUtc.AddHours(1)));
        await SaveAsync();
        _db.ChangeTracker.Clear();

        var persisted = await _repository.TryPersistServerEnrollmentAsync(
            1,
            new Server { Name = "runner", Hostname = "runner" },
            new ServerToken { TokenHash = "fresh", ExpiresAt = NowUtc.AddDays(30) },
            Ct);

        Assert.False(persisted);
        Assert.Empty(_db.ServerTokens);
    }

    [Fact]
    public async Task TryPersistServerEnrollmentAsync_RefusesAnUnknownRegistrationToken()
    {
        Assert.False(await _repository.TryPersistServerEnrollmentAsync(
            404,
            new Server { Name = "runner", Hostname = "runner" },
            new ServerToken { TokenHash = "fresh", ExpiresAt = NowUtc.AddDays(30) },
            Ct));
    }

    // ---------- server tokens ----------

    [Fact]
    public void AddServer_AndAddServerToken_OnlyStageTheInsert()
    {
        _repository.AddServer(new Server { Id = 5, Name = "runner", Hostname = "runner" });
        _repository.AddServerToken(Token(10, 5, "fresh"));

        Assert.Empty(_db.Servers);
        Assert.Empty(_db.ServerTokens);
        Assert.Equal(2, _db.ChangeTracker.Entries().Count(entry => entry.State == EntityState.Added));
    }

    [Fact]
    public async Task GetLatestActiveServerTokenExpiryAsync_TakesTheFurthestLivingExpiry()
    {
        _db.ServerTokens.AddRange(
            Token(1, 5, "near", expiresAt: NowUtc.AddDays(1)),
            Token(2, 5, "far", expiresAt: NowUtc.AddDays(30)),
            Token(3, 5, "revoked-further", revoked: true, expiresAt: NowUtc.AddDays(90)),
            Token(4, 5, "expired", expiresAt: NowUtc.AddDays(-1)),
            Token(5, 6, "other-server", expiresAt: NowUtc.AddDays(365)));
        await SaveAsync();

        Assert.Equal(NowUtc.AddDays(30), await _repository.GetLatestActiveServerTokenExpiryAsync(5, Ct));
        Assert.Null(await _repository.GetLatestActiveServerTokenExpiryAsync(7, Ct));
    }

    [Fact]
    public async Task DeleteExpiredServerTokensAsync_StagesTheRevokedAndExpiredTokensOfThatServer()
    {
        _db.ServerTokens.AddRange(
            Token(1, 5, "alive"),
            Token(2, 5, "revoked", revoked: true),
            Token(3, 5, "expired", expiresAt: NowUtc.AddSeconds(-1)),
            Token(4, 6, "other-server-revoked", revoked: true));
        await SaveAsync();

        var swept = await _repository.DeleteExpiredServerTokensAsync(5, Ct);

        Assert.Equal(2, swept);
        Assert.Equal(4, await _db.ServerTokens.CountAsync(Ct));

        await SaveAsync();
        _db.ChangeTracker.Clear();
        Assert.Equal(
            ["alive", "other-server-revoked"],
            (await _db.ServerTokens.AsNoTracking().ToListAsync(Ct))
                .Select(token => token.TokenHash).Order());
    }

    [Fact]
    public async Task DeleteExpiredServerTokensAsync_ReportsZeroWhenNothingIsDead()
    {
        _db.ServerTokens.Add(Token(1, 5, "alive"));
        await SaveAsync();

        Assert.Equal(0, await _repository.DeleteExpiredServerTokensAsync(5, Ct));
    }

    [Fact]
    public async Task ServerExistsAsync_AndServerHasActiveTokensAsync_AnswerIndependently()
    {
        _db.Servers.Add(new Server { Id = 5, Name = "runner", Hostname = "runner" });
        _db.ServerTokens.AddRange(
            Token(1, 5, "revoked", revoked: true),
            Token(2, 5, "expired", expiresAt: NowUtc.AddSeconds(-1)));
        await SaveAsync();

        Assert.True(await _repository.ServerExistsAsync(5, Ct));
        Assert.False(await _repository.ServerExistsAsync(6, Ct));
        Assert.False(await _repository.ServerHasActiveTokensAsync(5, Ct));

        _db.ServerTokens.Add(Token(3, 5, "alive"));
        await SaveAsync();

        Assert.True(await _repository.ServerHasActiveTokensAsync(5, Ct));
    }

    [Fact]
    public async Task ConsumeRegistrationTokenAsync_MarksTheTokenUsedOnceAndRefusesAReplay()
    {
        _db.RegistrationTokens.Add(Registration(1));
        await SaveAsync();
        _db.ChangeTracker.Clear();

        Assert.True(await _repository.ConsumeRegistrationTokenAsync(1, 5, Ct));
        _db.ChangeTracker.Clear();
        var token = await _db.RegistrationTokens.AsNoTracking().FirstAsync(Ct);
        Assert.True(token.IsUsed);
        Assert.Equal(5, token.UsedByServerId);

        Assert.False(await _repository.ConsumeRegistrationTokenAsync(1, 5, Ct));
        Assert.False(await _repository.ConsumeRegistrationTokenAsync(404, 5, Ct));
    }

    // ---------- refresh tokens ----------

    [Fact]
    public async Task RevokeRefreshTokenAsync_RevokesOnlyAnActiveToken_SoASecondRotationLoses()
    {
        _db.RefreshTokens.Add(Refresh(1, 100, NowUtc.AddDays(7)));
        await SaveAsync();

        Assert.True(await _repository.RevokeRefreshTokenAsync(1, 2, Ct));
        Assert.False(await _repository.RevokeRefreshTokenAsync(1, 3, Ct));
        Assert.False(await _repository.RevokeRefreshTokenAsync(404, 3, Ct));

        var state = await _repository.FindRefreshTokenStateAsync(1, Ct);
        Assert.Equal(NowUtc, state!.RevokedAt);
        Assert.Equal(2, state.ReplacedById); // the winner's replacement is kept
    }

    [Fact]
    public async Task DeleteExpiredRefreshTokensAsync_StagesTheExpiredAndLongRevokedTokensOfOneUser()
    {
        _db.RefreshTokens.AddRange(
            Refresh(1, 100, NowUtc.AddDays(7)),
            Refresh(2, 100, NowUtc.AddSeconds(-1)),
            Refresh(3, 100, NowUtc.AddDays(7), revokedAt: NowUtc.AddMinutes(-10)),
            Refresh(4, 100, NowUtc.AddDays(7), revokedAt: NowUtc.AddMinutes(-1)),
            Refresh(5, 101, NowUtc.AddSeconds(-1)));
        await SaveAsync();

        await _repository.DeleteExpiredRefreshTokensAsync(100, Ct);
        await SaveAsync();
        _db.ChangeTracker.Clear();

        Assert.Equal(
            [1, 4, 5],
            (await _db.RefreshTokens.AsNoTracking().ToListAsync(Ct)).Select(token => token.Id).Order());
    }

    [Fact]
    public async Task DeleteExpiredRefreshTokensAsync_StagesNothingWhenEveryTokenIsStillUsable()
    {
        _db.RefreshTokens.Add(Refresh(1, 100, NowUtc.AddDays(7)));
        await SaveAsync();

        await _repository.DeleteExpiredRefreshTokensAsync(100, Ct);
        await SaveAsync();

        Assert.Single(_db.RefreshTokens);
    }

    [Fact]
    public async Task DeleteAllExpiredRefreshTokensAsync_SweepsEveryUserAndCommitsImmediately()
    {
        _db.RefreshTokens.AddRange(
            Refresh(1, 100, NowUtc.AddDays(7)),
            Refresh(2, 100, NowUtc.AddSeconds(-1)),
            Refresh(3, 101, NowUtc.AddDays(7), revokedAt: NowUtc.AddMinutes(-10)),
            Refresh(4, 101, NowUtc.AddDays(7), revokedAt: NowUtc.AddMinutes(-1)));
        await SaveAsync();
        _db.ChangeTracker.Clear();

        var swept = await _repository.DeleteAllExpiredRefreshTokensAsync(NowUtc, Ct);
        _db.ChangeTracker.Clear();

        Assert.Equal(2, swept);
        Assert.Equal(
            [1, 4],
            (await _db.RefreshTokens.AsNoTracking().ToListAsync(Ct)).Select(token => token.Id).Order());
    }

    [Fact]
    public async Task DeleteAllExpiredRefreshTokensAsync_ReportsZeroWhenNothingIsDead()
    {
        _db.RefreshTokens.Add(Refresh(1, 100, NowUtc.AddDays(7)));
        await SaveAsync();

        Assert.Equal(0, await _repository.DeleteAllExpiredRefreshTokensAsync(NowUtc, Ct));
    }

    public void Dispose() => _db.Dispose();
}
