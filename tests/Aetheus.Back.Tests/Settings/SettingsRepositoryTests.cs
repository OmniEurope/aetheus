// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Settings;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.Tests;

public class SettingsRepositoryTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly SettingsRepository _repo;

    public SettingsRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _repo = new SettingsRepository(_db);
    }

    [Fact]
    public async Task GetAllSettingsAsync_ReturnsAll()
    {
        _db.AppSettings.AddRange(
            new AppSetting { Key = "Theme", Value = "dark" },
            new AppSetting { Key = "Lang", Value = "fr" }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetAllSettingsAsync(ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task FindSettingByKeyAsync_Found()
    {
        _db.AppSettings.Add(new AppSetting { Key = "SiteName", Value = "Aetheus" });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.FindSettingByKeyAsync("SiteName", ct: TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Equal("Aetheus", result.Value);
    }

    [Fact]
    public async Task FindSettingByKeyAsync_NotFound()
    {
        var result = await _repo.FindSettingByKeyAsync("Missing", ct: TestContext.Current.CancellationToken);
        Assert.Null(result);
    }

    [Fact]
    public async Task AddSettingAsync_Persists()
    {
        await _repo.AddSettingAsync(new AppSetting { Key = "New", Value = "val" }, ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, await _db.AppSettings.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetAllSecretsAsync_ReturnsAll()
    {
        _db.Secrets.AddRange(
            new Secret { Key = "DB_PASS", EncryptedValue = "enc1" },
            new Secret { Key = "API_KEY", EncryptedValue = "enc2" }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetAllSecretsAsync(ct: TestContext.Current.CancellationToken);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task FindSecretByKeyAsync_Found()
    {
        _db.Secrets.Add(new Secret { Key = "DB_PASS", EncryptedValue = "enc" });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.FindSecretByKeyAsync("DB_PASS", ct: TestContext.Current.CancellationToken);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task FindSecretByKeyAsync_NotFound()
    {
        var result = await _repo.FindSecretByKeyAsync("NONE", ct: TestContext.Current.CancellationToken);
        Assert.Null(result);
    }

    [Fact]
    public async Task FindSecretByIdAsync_Found()
    {
        var s = new Secret { Key = "KEY", EncryptedValue = "enc" };
        _db.Secrets.Add(s);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.FindSecretByIdAsync(s.Id, ct: TestContext.Current.CancellationToken);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task FindSecretByIdAsync_NotFound()
    {
        var result = await _repo.FindSecretByIdAsync(999, ct: TestContext.Current.CancellationToken);
        Assert.Null(result);
    }

    [Fact]
    public async Task AddSecretAsync_Persists()
    {
        await _repo.AddSecretAsync(new Secret { Key = "NEW", EncryptedValue = "e" }, ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, await _db.Secrets.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RemoveSecretAsync_Deletes()
    {
        var s = new Secret { Key = "DEL", EncryptedValue = "e" };
        _db.Secrets.Add(s);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        await _repo.RemoveSecretAsync(s, ct: TestContext.Current.CancellationToken);
        Assert.Equal(0, await _db.Secrets.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SaveChangesAsync_PersistsChanges()
    {
        _db.AppSettings.Add(new AppSetting { Key = "K", Value = "V" });
        await _repo.SaveChangesAsync(ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, await _db.AppSettings.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    public void Dispose() => _db.Dispose();
}
