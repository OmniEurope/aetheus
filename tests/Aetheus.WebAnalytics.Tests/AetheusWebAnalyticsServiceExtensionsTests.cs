// SPDX-License-Identifier: EUPL-1.2
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Aetheus.WebAnalytics.Tests;

public sealed class AetheusWebAnalyticsServiceExtensionsTests : IDisposable
{
    private readonly string? _originalEnabled =
        Environment.GetEnvironmentVariable("AETHEUS_WEB_ANALYTICS_ENABLED");

    [Fact]
    public void AddAetheusWebAnalytics_Disabled_RegistersNoExporter()
    {
        Environment.SetEnvironmentVariable("AETHEUS_WEB_ANALYTICS_ENABLED", null);
        var services = new ServiceCollection();

        services.AddAetheusWebAnalytics(Configuration(("Aetheus:WebAnalytics:Enabled", "false")));

        using var provider = services.BuildServiceProvider();
        Assert.False(provider.GetRequiredService<AetheusWebAnalyticsOptions>().Enabled);
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(AnalyticsExportQueue));
    }

    [Fact]
    public void AddAetheusWebAnalytics_Enabled_IsIdempotentAndKeepsPrivacyPageExplicit()
    {
        Environment.SetEnvironmentVariable("AETHEUS_WEB_ANALYTICS_ENABLED", "true");
        var services = new ServiceCollection();
        var configuration = ValidConfiguration();

        services.AddAetheusWebAnalytics(configuration);
        var count = services.Count;
        services.AddAetheusWebAnalytics(configuration);

        Assert.Equal(count, services.Count);
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<AetheusWebAnalyticsOptions>();
        Assert.True(options.Enabled);
        Assert.False(options.EnablePrivacyPage);
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(AnalyticsExportQueue));
    }

    [Fact]
    public void AddAetheusWebAnalytics_PrivacyPageFailsClosedWhenRequiredTextIsMissing()
    {
        Environment.SetEnvironmentVariable("AETHEUS_WEB_ANALYTICS_ENABLED", "true");
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddAetheusWebAnalytics(ValidConfiguration(
                ("Aetheus:WebAnalytics:EnablePrivacyPage", "true"))));

        Assert.Contains("privacy page", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose() =>
        Environment.SetEnvironmentVariable("AETHEUS_WEB_ANALYTICS_ENABLED", _originalEnabled);

    private static IConfiguration ValidConfiguration(params (string Key, string Value)[] overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["Aetheus:WebAnalytics:Enabled"] = "true",
            ["Aetheus:WebAnalytics:ApplicationId"] = "42",
            ["Aetheus:WebAnalytics:SiteId"] = "portfolio-prod",
            ["Aetheus:WebAnalytics:IngestEndpoint"] =
                "https://aetheus.example/api/ingest/web-analytics/v1/events",
            ["Aetheus:WebAnalytics:IngestKey"] = "ingest-secret",
            ["Aetheus:WebAnalytics:PseudonymizationKey"] =
                "0123456789abcdef0123456789abcdef"
        };
        foreach (var (key, value) in overrides)
            values[key] = value;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static IConfiguration Configuration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(item => item.Key, item => (string?)item.Value))
            .Build();
}
