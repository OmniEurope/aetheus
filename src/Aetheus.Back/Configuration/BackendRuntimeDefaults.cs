// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Configuration;

/// <summary>Operational timings shared by backend infrastructure services.</summary>
public static class BackendRuntimeDefaults
{
    public static readonly TimeSpan InfrastructureCacheDuration = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan ReferenceDataCacheDuration = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan SchedulerCheckInterval = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan GitProcessTimeout = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan GitWriteTimeout = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan MaintenanceInterval = TimeSpan.FromHours(1);
    public static readonly TimeSpan RateLimitWindow = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan JwtClockSkew = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan CorsPreflightMaxAge = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan OutboundRequestTimeout = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan RegistryRequestTimeout = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan GitLongRunningTimeout = TimeSpan.FromMinutes(10);
}
