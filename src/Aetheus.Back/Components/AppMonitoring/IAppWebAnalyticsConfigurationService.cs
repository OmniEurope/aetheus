// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.AppMonitoring;

public readonly record struct PublicAnalyticsContext(MonitoredApp App, string PseudonymizationKey);

public interface IAppWebAnalyticsConfigurationService
{
    Task<AppWebAnalyticsConfigurationDto?> GetAsync(
        int appId,
        CancellationToken ct = default);
    Task<AppWebAnalyticsConfigurationDto?> ConfigureAsync(
        int appId,
        ConfigureAppWebAnalyticsRequest request,
        CancellationToken ct = default);
    Task<AppWebAnalyticsConfigurationDto?> RotateKeyAsync(int appId, CancellationToken ct = default);
    Task<PublicAnalyticsContext?> ResolvePublicContextAsync(
        string siteId,
        string origin,
        CancellationToken ct = default);
    Task<(int Rotated, int PurgedVersions)> MaintainKeysAsync(
        DateTime nowUtc,
        CancellationToken ct = default);
}
