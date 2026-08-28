// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.AppMonitoring;
using Aetheus.Back.Components.Audit;
using Aetheus.Back.Components.Vaults;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Aetheus.Back.Tests.AppMonitoring;

public sealed class AppWebAnalyticsConfigurationServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IVaultService _vaults = Substitute.For<IVaultService>();
    private readonly IAuditService _audit = Substitute.For<IAuditService>();
    private readonly FakeTimeProvider _time =
        new(new DateTimeOffset(2026, 7, 23, 12, 0, 0, TimeSpan.Zero));
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly AppWebAnalyticsConfigurationService _service;

    public AppWebAnalyticsConfigurationServiceTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        _db.MonitoredApps.Add(new MonitoredApp
        {
            Id = 7,
            ProjectId = 3,
            Name = "portfolio",
            Enabled = true
        });
        _db.SaveChanges();
        _vaults.CreateVaultAsync(Arg.Any<CreateVaultRequest>(), Arg.Any<CancellationToken>())
            .Returns(new VaultDto { Id = 42, Name = "aetheus-web-analytics-7", ProjectId = 3 });
        _vaults.CreateSecretAsync(42, Arg.Any<CreateVaultSecretRequest>(), Arg.Any<CancellationToken>())
            .Returns(new VaultSecretDto { Id = 8, Key = AppWebAnalyticsConfigurationService.SecretKey });
        _service = new AppWebAnalyticsConfigurationService(
            new AppMonitoringRepository(_db),
            _vaults,
            _audit,
            _cache,
            _time);
    }

    [Fact]
    public async Task Configure_CreatesPerApplicationVaultAndPersistsOnlyMetadata()
    {
        var configured = await _service.ConfigureAsync(7, new ConfigureAppWebAnalyticsRequest
        {
            Enabled = true,
            PublicIngestEnabled = true,
            SiteId = "portfolio-prod",
            AllowedOrigins = ["https://www.example.com", "https://www.example.com/"],
            StorageBudgetBytes = 128 * 1024 * 1024
        }, TestContext.Current.CancellationToken);

        Assert.NotNull(configured);
        Assert.Equal(["https://www.example.com"], configured.AllowedOrigins);
        var app = await _db.MonitoredApps.AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("aetheus-web-analytics-7", app.AnalyticsVaultName);
        Assert.Equal(1, app.AnalyticsPseudonymKeyVersion);
        Assert.DoesNotContain(
            AppWebAnalyticsConfigurationService.SecretKey,
            app.AnalyticsAllowedOriginsJson ?? string.Empty,
            StringComparison.Ordinal);
        Assert.DoesNotContain("unit-test", app.AnalyticsAllowedOriginsJson ?? string.Empty, StringComparison.Ordinal);
        await _vaults.Received(1).CreateSecretAsync(
            42,
            Arg.Is<CreateVaultSecretRequest>(request =>
                request.Key == AppWebAnalyticsConfigurationService.SecretKey
                && Convert.FromBase64String(request.Value).Length == 32),
            Arg.Any<CancellationToken>());
        await _audit.Received(1).LogAsync(
            "ConfiguredWebAnalytics",
            "MonitoredApp",
            7,
            Arg.Is<string>(details => !details.Contains("AETHEUS_WEB_ANALYTICS_PSEUDONYMIZATION_KEY", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Configure_RejectsNonHttpsOrPathOriginsBeforeCreatingVault()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ConfigureAsync(
            7,
            new ConfigureAppWebAnalyticsRequest
            {
                Enabled = true,
                PublicIngestEnabled = true,
                SiteId = "portfolio-prod",
                AllowedOrigins = ["http://example.com/private"]
            },
            TestContext.Current.CancellationToken));

        await _vaults.DidNotReceive().CreateVaultAsync(
            Arg.Any<CreateVaultRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Configure_SecretFailureCompensatesNewVault()
    {
        _vaults.CreateSecretAsync(42, Arg.Any<CreateVaultSecretRequest>(), Arg.Any<CancellationToken>())
            .Returns<VaultSecretDto>(_ => throw new InvalidOperationException("vault write failed"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ConfigureAsync(
            7,
            new ConfigureAppWebAnalyticsRequest
            {
                Enabled = true,
                SiteId = "portfolio-prod",
                AllowedOrigins = ["https://www.example.com"]
            },
            TestContext.Current.CancellationToken));

        await _vaults.Received(1).DeleteVaultAsync(42, CancellationToken.None);
        var app = await _db.MonitoredApps.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Null(app.AnalyticsVaultName);
        Assert.Equal(0, app.AnalyticsPseudonymKeyVersion);
    }

    [Fact]
    public async Task MaintainKeys_ResumesPendingRotationAndPurgesVersionsOlderThanThirtyOneDays()
    {
        var app = await _db.MonitoredApps.SingleAsync(TestContext.Current.CancellationToken);
        app.AnalyticsVaultName = "aetheus-web-analytics-7";
        app.AnalyticsPseudonymKeyVersion = 1;
        app.AnalyticsPseudonymKeyCreatedAt = _time.GetUtcNow().UtcDateTime.AddMonths(-12);
        app.AnalyticsPendingPseudonymKeyVersion = 2;
        app.AnalyticsKeyRotationPendingAt = _time.GetUtcNow().UtcDateTime.AddMinutes(-5);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);
        _vaults.GetVaultsAsync(3, null, null, Arg.Any<PaginationRequest>(), null, Arg.Any<CancellationToken>())
            .Returns(new PaginatedResult<VaultDto>
            {
                Items = [new VaultDto { Id = 42, Name = app.AnalyticsVaultName }],
                TotalCount = 1
            });
        _vaults.GetVaultDetailAsync(42, Arg.Any<CancellationToken>())
            .Returns(new VaultDetailDto
            {
                Id = 42,
                Name = app.AnalyticsVaultName,
                Secrets = [new VaultSecretDto { Id = 8, Key = AppWebAnalyticsConfigurationService.SecretKey }]
            });
        _vaults.RotateSecretAsync(42, 8, Arg.Any<RotateVaultSecretRequest>(), Arg.Any<CancellationToken>())
            .Returns(new VaultSecretDto { Id = 8, Key = AppWebAnalyticsConfigurationService.SecretKey });
        _vaults.PurgeHistoricalSecretVersionsAsync(
                42,
                8,
                Arg.Any<DateTime>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(3);

        var result = await _service.MaintainKeysAsync(
            _time.GetUtcNow().UtcDateTime,
            TestContext.Current.CancellationToken);

        Assert.Equal((1, 3), result);
        var persisted = await _db.MonitoredApps.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, persisted.AnalyticsPseudonymKeyVersion);
        Assert.Null(persisted.AnalyticsPendingPseudonymKeyVersion);
        await _vaults.Received(1).PurgeHistoricalSecretVersionsAsync(
            42,
            8,
            _time.GetUtcNow().UtcDateTime.AddDays(-31),
            AppWebAnalyticsConfigurationService.MaximumPurgedVersionsPerSecret,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolvePublicContext_RequiresApplicationAnalyticsAndPublicIngestToRemainEnabled()
    {
        _vaults.ResolveVaultSecretsAsync(
                Arg.Any<List<string>>(),
                3,
                Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, string>
            {
                [AppWebAnalyticsConfigurationService.SecretKey] = "vault-secret"
            });
        var enabled = new ConfigureAppWebAnalyticsRequest
        {
            Enabled = true,
            PublicIngestEnabled = true,
            SiteId = "portfolio-prod",
            AllowedOrigins = ["https://www.example.com"]
        };
        await _service.ConfigureAsync(7, enabled, TestContext.Current.CancellationToken);

        var active = await _service.ResolvePublicContextAsync(
            "portfolio-prod",
            "https://www.example.com",
            TestContext.Current.CancellationToken);
        var wrongOrigin = await _service.ResolvePublicContextAsync(
            "portfolio-prod",
            "https://attacker.example",
            TestContext.Current.CancellationToken);
        await _service.ConfigureAsync(
            7,
            enabled with { PublicIngestEnabled = false },
            TestContext.Current.CancellationToken);
        var disabled = await _service.ResolvePublicContextAsync(
            "portfolio-prod",
            "https://www.example.com",
            TestContext.Current.CancellationToken);

        Assert.Equal("vault-secret", active?.PseudonymizationKey);
        Assert.Null(wrongOrigin);
        Assert.Null(disabled);
    }

    [Fact]
    public async Task ResolvePublicContext_CachesVaultKeyAndRotationInvalidatesIt()
    {
        var enabled = new ConfigureAppWebAnalyticsRequest
        {
            Enabled = true,
            PublicIngestEnabled = true,
            SiteId = "portfolio-prod",
            AllowedOrigins = ["https://www.example.com"]
        };
        await _service.ConfigureAsync(7, enabled, TestContext.Current.CancellationToken);
        _vaults.ResolveVaultSecretsAsync(
                Arg.Any<List<string>>(),
                3,
                Arg.Any<CancellationToken>())
            .Returns(
                new Dictionary<string, string>
                {
                    [AppWebAnalyticsConfigurationService.SecretKey] = "first-key"
                },
                new Dictionary<string, string>
                {
                    [AppWebAnalyticsConfigurationService.SecretKey] = "rotated-key"
                });
        _vaults.GetVaultsAsync(3, null, null, Arg.Any<PaginationRequest>(), null, Arg.Any<CancellationToken>())
            .Returns(new PaginatedResult<VaultDto>
            {
                Items = [new VaultDto { Id = 42, Name = "aetheus-web-analytics-7" }],
                TotalCount = 1
            });
        _vaults.GetVaultDetailAsync(42, Arg.Any<CancellationToken>())
            .Returns(new VaultDetailDto
            {
                Id = 42,
                Name = "aetheus-web-analytics-7",
                Secrets = [new VaultSecretDto { Id = 8, Key = AppWebAnalyticsConfigurationService.SecretKey }]
            });
        _vaults.RotateSecretAsync(42, 8, Arg.Any<RotateVaultSecretRequest>(), Arg.Any<CancellationToken>())
            .Returns(new VaultSecretDto { Id = 8, Key = AppWebAnalyticsConfigurationService.SecretKey });

        var first = await _service.ResolvePublicContextAsync(
            "portfolio-prod", "https://www.example.com", TestContext.Current.CancellationToken);
        var cached = await _service.ResolvePublicContextAsync(
            "portfolio-prod", "https://www.example.com", TestContext.Current.CancellationToken);
        await _service.RotateKeyAsync(7, TestContext.Current.CancellationToken);
        var rotated = await _service.ResolvePublicContextAsync(
            "portfolio-prod", "https://www.example.com", TestContext.Current.CancellationToken);

        Assert.Equal("first-key", first?.PseudonymizationKey);
        Assert.Equal("first-key", cached?.PseudonymizationKey);
        Assert.Equal("rotated-key", rotated?.PseudonymizationKey);
        await _vaults.Received(2).ResolveVaultSecretsAsync(
            Arg.Any<List<string>>(),
            3,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MaintenanceQueries_SelectOnlyDueRotationsAndPageConfiguredApps()
    {
        _db.MonitoredApps.AddRange(
            new MonitoredApp
            {
                Id = 8,
                ProjectId = 3,
                Name = "due",
                AnalyticsVaultName = "due-vault",
                AnalyticsPseudonymKeyCreatedAt = _time.GetUtcNow().UtcDateTime.AddMonths(-13)
            },
            new MonitoredApp
            {
                Id = 9,
                ProjectId = 3,
                Name = "fresh",
                AnalyticsVaultName = "fresh-vault",
                AnalyticsPseudonymKeyCreatedAt = _time.GetUtcNow().UtcDateTime
            },
            new MonitoredApp
            {
                Id = 10,
                ProjectId = 3,
                Name = "pending",
                AnalyticsVaultName = "pending-vault",
                AnalyticsPseudonymKeyCreatedAt = _time.GetUtcNow().UtcDateTime,
                AnalyticsPendingPseudonymKeyVersion = 2
            });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = new AppMonitoringRepository(_db);

        var candidates = await repository.GetAnalyticsKeyRotationCandidatesAsync(
            _time.GetUtcNow().UtcDateTime.AddMonths(-12),
            10,
            TestContext.Current.CancellationToken);
        var page = await repository.GetAnalyticsConfiguredAppsPageAsync(
            afterId: 8,
            maxCount: 1,
            ct: TestContext.Current.CancellationToken);

        Assert.Equal([8, 10], candidates.Select(app => app.Id));
        Assert.Equal(9, Assert.Single(page).Id);
    }

    public void Dispose()
    {
        _cache.Dispose();
        _db.Dispose();
    }
}
