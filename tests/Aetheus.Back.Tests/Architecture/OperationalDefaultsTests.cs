// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Configuration;
using Aetheus.Shared.Constants;
using Aetheus.Shared.Enums;

namespace Aetheus.Back.Tests.Architecture;

public sealed class OperationalDefaultsTests
{
    [Theory]
    [InlineData(PackageFeedType.NuGet, PackageRegistryDefaults.NuGet)]
    [InlineData(PackageFeedType.Npm, PackageRegistryDefaults.Npm)]
    [InlineData(PackageFeedType.PyPI, PackageRegistryDefaults.PyPi)]
    public void PackageRegistryDefaults_MapEveryFeedType(PackageFeedType type, string expected)
    {
        Assert.Equal(expected, PackageRegistryDefaults.For(type));
        Assert.True(Uri.TryCreate(expected, UriKind.Absolute, out _));
    }

    [Theory]
    [InlineData(-1, PaginationDefaults.MinimumPageSize)]
    [InlineData(PaginationDefaults.DefaultPageSize, PaginationDefaults.DefaultPageSize)]
    [InlineData(999, PaginationDefaults.MaximumPageSize)]
    public void PaginationDefaults_ClampToSharedBounds(int requested, int expected) =>
        Assert.Equal(expected, PaginationDefaults.Clamp(requested));

    [Fact]
    public void AppMonitoringDefaults_HaveConsistentBounds()
    {
        Assert.InRange(
            AppMonitoringDefaults.DefaultProbeIntervalSeconds,
            AppMonitoringDefaults.MinimumProbeIntervalSeconds,
            AppMonitoringDefaults.MaximumProbeIntervalSeconds);
        Assert.InRange(
            AppMonitoringDefaults.DefaultProbeTimeoutSeconds,
            AppMonitoringDefaults.MinimumProbeTimeoutSeconds,
            AppMonitoringDefaults.MaximumProbeTimeoutSeconds);
        Assert.InRange(
            AppMonitoringDefaults.DefaultRawRetentionDays,
            AppMonitoringDefaults.MinimumRawRetentionDays,
            AppMonitoringDefaults.MaximumRawRetentionDays);
    }

    [Fact]
    public void BackendRuntimeDefaults_ArePositiveAndOrdered()
    {
        Assert.True(BackendRuntimeDefaults.InfrastructureCacheDuration > TimeSpan.Zero);
        Assert.True(BackendRuntimeDefaults.ReferenceDataCacheDuration > BackendRuntimeDefaults.InfrastructureCacheDuration);
        Assert.True(BackendRuntimeDefaults.GitWriteTimeout >= BackendRuntimeDefaults.GitProcessTimeout);
        Assert.True(BackendRuntimeDefaults.MaintenanceInterval > BackendRuntimeDefaults.SchedulerCheckInterval);
    }
}
