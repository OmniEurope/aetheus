// SPDX-License-Identifier: EUPL-1.2
using System.Security.Cryptography;
using System.Text.Json;
using Aetheus.Back.Components.Vaults;
using Microsoft.Extensions.Caching.Memory;

namespace Aetheus.Back.Components.AppMonitoring;

public sealed class AppWebAnalyticsConfigurationService(
    IAppMonitoringRepository apps,
    IVaultService vaults,
    IAuditService audit,
    IMemoryCache cache,
    TimeProvider timeProvider) : IAppWebAnalyticsConfigurationService
{
    internal const string SecretKey = "AETHEUS_WEB_ANALYTICS_PSEUDONYMIZATION_KEY";
    internal const int MaintenanceBatchSize = 50;
    internal const int MaximumMaintenanceAppsPerSweep = 100;
    internal const int MaximumPurgedVersionsPerSecret = 100;
    private static readonly TimeSpan PublicKeyCacheLifetime = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaintenanceTimeBudget = TimeSpan.FromSeconds(15);
    private const string MaintenanceCursorCacheKey = "web-analytics-key-maintenance-cursor";

    public async Task<AppWebAnalyticsConfigurationDto?> GetAsync(
        int appId,
        CancellationToken ct = default)
    {
        var app = await apps.GetAppAsync(appId, ct).ConfigureAwait(false);
        return app is null
            ? null
            : Map(app, ReadOrigins(app.AnalyticsAllowedOriginsJson));
    }

    public async Task<AppWebAnalyticsConfigurationDto?> ConfigureAsync(
        int appId,
        ConfigureAppWebAnalyticsRequest request,
        CancellationToken ct = default)
    {
        var app = await apps.GetAppForUpdateAsync(appId, ct).ConfigureAwait(false);
        if (app is null)
            return null;
        InvalidatePublicKey(app.Id);

        var existingSite = await apps.GetAppByAnalyticsSiteIdAsync(request.SiteId, ct).ConfigureAwait(false);
        if (existingSite is not null && existingSite.Id != appId)
            throw new InvalidOperationException("This analytics site id is already assigned.");

        var origins = NormalizeOrigins(request.AllowedOrigins);
        int? provisionedVaultId = null;
        try
        {
            if (app.AnalyticsVaultName is null)
            {
                var vaultName = $"aetheus-web-analytics-{app.Id}";
                var vault = await vaults.CreateVaultAsync(new CreateVaultRequest
                {
                    Name = vaultName,
                    Description = "Aetheus-managed pseudonymization keys for no-banner web analytics.",
                    ProjectId = app.ProjectId
                }, ct).ConfigureAwait(false);
                provisionedVaultId = vault.Id;
                await vaults.CreateSecretAsync(vault.Id, new CreateVaultSecretRequest
                {
                    Key = SecretKey,
                    Value = GenerateKey()
                }, ct).ConfigureAwait(false);
                app.AnalyticsVaultName = vaultName;
                app.AnalyticsPseudonymKeyVersion = 1;
                app.AnalyticsPseudonymKeyCreatedAt = timeProvider.GetUtcNow().UtcDateTime;
            }

            app.AnalyticsSiteId = request.SiteId;
            app.AnalyticsAllowedOriginsJson = JsonSerializer.Serialize(origins);
            app.AnalyticsEnabled = request.Enabled;
            app.AnalyticsPublicIngestEnabled = request.PublicIngestEnabled;
            app.AnalyticsStorageBudgetBytes = request.StorageBudgetBytes;
            await apps.SaveChangesAsync(ct).ConfigureAwait(false);
            provisionedVaultId = null;
        }
        catch
        {
            if (provisionedVaultId is { } vaultId)
                await vaults.DeleteVaultAsync(vaultId, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        InvalidatePublicKey(app.Id);
        await audit.LogAsync(
            "ConfiguredWebAnalytics",
            "MonitoredApp",
            app.Id,
            $"Site {app.AnalyticsSiteId}; public ingest {app.AnalyticsPublicIngestEnabled}; key version {app.AnalyticsPseudonymKeyVersion}",
            ct).ConfigureAwait(false);
        return Map(app, origins);
    }

    public async Task<AppWebAnalyticsConfigurationDto?> RotateKeyAsync(
        int appId,
        CancellationToken ct = default)
    {
        var app = await apps.GetAppForUpdateAsync(appId, ct).ConfigureAwait(false);
        if (app?.AnalyticsVaultName is null)
            return null;
        InvalidatePublicKey(app.Id);

        var vaultPage = await vaults.GetVaultsAsync(
            app.ProjectId,
            request: new PaginationRequest { Search = app.AnalyticsVaultName, PageSize = 20 },
            ct: ct).ConfigureAwait(false);
        var vault = vaultPage.Items.SingleOrDefault(item =>
            string.Equals(item.Name, app.AnalyticsVaultName, StringComparison.Ordinal));
        if (vault is null)
            throw new InvalidOperationException("The analytics key vault no longer exists.");
        var detail = await vaults.GetVaultDetailAsync(vault.Id, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The analytics key vault no longer exists.");
        var secret = detail.Secrets.SingleOrDefault(item =>
            string.Equals(item.Key, SecretKey, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("The analytics pseudonymization key no longer exists.");

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (app.AnalyticsPendingPseudonymKeyVersion is null)
        {
            app.AnalyticsPendingPseudonymKeyVersion = app.AnalyticsPseudonymKeyVersion + 1;
            app.AnalyticsKeyRotationPendingAt = now;
            await apps.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        await vaults.RotateSecretAsync(vault.Id, secret.Id, new RotateVaultSecretRequest
        {
            Value = GenerateKey()
        }, ct).ConfigureAwait(false);
        app.AnalyticsPseudonymKeyVersion = app.AnalyticsPendingPseudonymKeyVersion.Value;
        app.AnalyticsPseudonymKeyCreatedAt = now;
        app.AnalyticsPendingPseudonymKeyVersion = null;
        app.AnalyticsKeyRotationPendingAt = null;
        await apps.SaveChangesAsync(ct).ConfigureAwait(false);
        InvalidatePublicKey(app.Id);
        await audit.LogAsync(
            "RotatedWebAnalyticsKey",
            "MonitoredApp",
            app.Id,
            $"Version {app.AnalyticsPseudonymKeyVersion}",
            ct).ConfigureAwait(false);
        return Map(app, ReadOrigins(app.AnalyticsAllowedOriginsJson));
    }

    public async Task<PublicAnalyticsContext?> ResolvePublicContextAsync(
        string siteId,
        string origin,
        CancellationToken ct = default)
    {
        var app = await apps.GetAppByAnalyticsSiteIdAsync(siteId, ct).ConfigureAwait(false);
        if (app?.AnalyticsVaultName is null
            || app.AnalyticsPendingPseudonymKeyVersion is not null
            || !app.Enabled
            || !app.AnalyticsEnabled
            || !app.AnalyticsPublicIngestEnabled)
            return null;
        var allowedOrigins = ReadOrigins(app.AnalyticsAllowedOriginsJson);
        if (!allowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase))
            return null;
        var cacheKey = PublicKeyCacheKey(app.Id);
        if (!cache.TryGetValue<CachedPublicKey>(cacheKey, out var cached)
            || cached is null
            || cached.KeyVersion != app.AnalyticsPseudonymKeyVersion)
        {
            var secrets = await vaults.ResolveVaultSecretsAsync(
                [app.AnalyticsVaultName],
                app.ProjectId,
                ct).ConfigureAwait(false);
            if (!secrets.TryGetValue(SecretKey, out var resolvedKey))
                return null;
            cached = new CachedPublicKey(app.AnalyticsPseudonymKeyVersion, resolvedKey);
            cache.Set(cacheKey, cached, PublicKeyCacheLifetime);
        }
        return cached is { } publicKey
            ? new PublicAnalyticsContext(app, publicKey.Value)
            : null;
    }

    public async Task<(int Rotated, int PurgedVersions)> MaintainKeysAsync(
        DateTime nowUtc,
        CancellationToken ct = default)
    {
        var rotated = 0;
        var purged = 0;
        var startedAt = timeProvider.GetTimestamp();
        var rotationCandidates = await apps.GetAnalyticsKeyRotationCandidatesAsync(
            nowUtc.AddMonths(-12),
            MaintenanceBatchSize,
            ct).ConfigureAwait(false);
        foreach (var configuredApp in rotationCandidates)
        {
            if (timeProvider.GetElapsedTime(startedAt) >= MaintenanceTimeBudget)
                break;
            if (await RotateKeyAsync(configuredApp.Id, ct).ConfigureAwait(false) is not null)
                rotated++;
        }

        var afterId = cache.Get<int>(MaintenanceCursorCacheKey);
        var visited = 0;
        var timeBudgetReached = false;
        while (visited < MaximumMaintenanceAppsPerSweep
               && timeProvider.GetElapsedTime(startedAt) < MaintenanceTimeBudget)
        {
            var pageSize = Math.Min(
                MaintenanceBatchSize,
                MaximumMaintenanceAppsPerSweep - visited);
            var configuredApps = await apps.GetAnalyticsConfiguredAppsPageAsync(
                afterId,
                pageSize,
                ct).ConfigureAwait(false);
            if (configuredApps.Count == 0)
            {
                cache.Remove(MaintenanceCursorCacheKey);
                break;
            }

            foreach (var configuredApp in configuredApps)
            {
                if (timeProvider.GetElapsedTime(startedAt) >= MaintenanceTimeBudget)
                {
                    timeBudgetReached = true;
                    break;
                }
                afterId = configuredApp.Id;
                visited++;
                cache.Set(MaintenanceCursorCacheKey, afterId);
                if (configuredApp.AnalyticsVaultName is null)
                    continue;
                var vaultPage = await vaults.GetVaultsAsync(
                    configuredApp.ProjectId,
                    request: new PaginationRequest
                    {
                        Search = configuredApp.AnalyticsVaultName,
                        PageSize = 20
                    },
                    ct: ct).ConfigureAwait(false);
                var vault = vaultPage.Items.SingleOrDefault(item =>
                    string.Equals(item.Name, configuredApp.AnalyticsVaultName, StringComparison.Ordinal));
                if (vault is null)
                    continue;
                var detail = await vaults.GetVaultDetailAsync(vault.Id, ct).ConfigureAwait(false);
                var secret = detail?.Secrets.SingleOrDefault(item =>
                    string.Equals(item.Key, SecretKey, StringComparison.Ordinal));
                if (secret is not null)
                {
                    purged += await vaults.PurgeHistoricalSecretVersionsAsync(
                        vault.Id,
                        secret.Id,
                        nowUtc.AddDays(-31),
                        MaximumPurgedVersionsPerSecret,
                        ct).ConfigureAwait(false);
                }
            }
            if (!timeBudgetReached && configuredApps.Count < pageSize)
            {
                cache.Remove(MaintenanceCursorCacheKey);
                break;
            }
            if (timeBudgetReached)
                break;
        }
        return (rotated, purged);
    }

    private static string PublicKeyCacheKey(int appId) => $"web-analytics-public-key:{appId}";

    private void InvalidatePublicKey(int appId) => cache.Remove(PublicKeyCacheKey(appId));

    private sealed record CachedPublicKey(int KeyVersion, string Value);

    private static List<string> NormalizeOrigins(IEnumerable<string> values) =>
        values.Select(value =>
            {
                if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
                    || uri.Scheme != Uri.UriSchemeHttps
                    || uri.AbsolutePath != "/"
                    || !string.IsNullOrEmpty(uri.Query)
                    || !string.IsNullOrEmpty(uri.Fragment))
                    throw new InvalidOperationException("Analytics origins must be HTTPS origins without a path.");
                return uri.GetLeftPart(UriPartial.Authority);
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static List<string> ReadOrigins(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? []
            : JsonSerializer.Deserialize<List<string>>(json) ?? [];

    private static string GenerateKey() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static AppWebAnalyticsConfigurationDto Map(
        Aetheus.Back.Data.Entities.MonitoredApp app,
        IReadOnlyList<string> origins) => new()
        {
            PublicIngestEnabled = app.AnalyticsPublicIngestEnabled,
            Enabled = app.AnalyticsEnabled,
            SiteId = app.AnalyticsSiteId ?? string.Empty,
            AllowedOrigins = origins,
            StorageBudgetBytes = app.AnalyticsStorageBudgetBytes,
            PseudonymKeyVersion = app.AnalyticsPseudonymKeyVersion,
            PseudonymKeyCreatedAt = app.AnalyticsPseudonymKeyCreatedAt
        };
}
