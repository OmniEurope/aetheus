// SPDX-License-Identifier: EUPL-1.2
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Aetheus.Telemetry.Tests;

public sealed class AetheusTelemetryBuilderExtensionsTests : IDisposable
{
    private readonly string? _originalEnabled = Environment.GetEnvironmentVariable("AETHEUS_TELEMETRY_ENABLED");

    [Fact]
    public void AddAetheusTelemetry_Disabled_RegistersOptionsWithoutProviders()
    {
        Environment.SetEnvironmentVariable("AETHEUS_TELEMETRY_ENABLED", null);
        var services = new ServiceCollection();

        services.AddAetheusTelemetry(Configuration(("Aetheus:Telemetry:Enabled", "false")));

        using var provider = services.BuildServiceProvider();
        Assert.False(provider.GetRequiredService<AetheusTelemetryOptions>().Enabled);
        Assert.DoesNotContain(services, descriptor =>
            descriptor.ServiceType.FullName?.Contains("TracerProvider", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void AddAetheusTelemetry_Enabled_RegistersOnceAndUsesBoundedOptions()
    {
        Environment.SetEnvironmentVariable("AETHEUS_TELEMETRY_ENABLED", "true");
        var services = new ServiceCollection();
        var configuration = ValidConfiguration();

        services.AddAetheusTelemetry(configuration);
        var countAfterFirstCall = services.Count;
        services.AddAetheusTelemetry(configuration);

        Assert.Equal(countAfterFirstCall, services.Count);
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<AetheusTelemetryOptions>();
        Assert.True(options.Enabled);
        Assert.Equal(0.25, options.TraceSampleRatio);
        Assert.Equal(1024, options.MaxExportQueueSize);
    }

    [Fact]
    public void AddAetheusTelemetry_RejectsHttpEndpointWhenEnabled()
    {
        Environment.SetEnvironmentVariable("AETHEUS_TELEMETRY_ENABLED", "true");
        var services = new ServiceCollection();
        var configuration = ValidConfiguration(("Aetheus:Telemetry:MetricsEndpoint", "http://aetheus/metrics"));

        var exception = Assert.Throws<InvalidOperationException>(
            () => services.AddAetheusTelemetry(configuration));

        Assert.Contains("HTTPS", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddAetheusTelemetry_RejectsInvalidSampleRatio()
    {
        Environment.SetEnvironmentVariable("AETHEUS_TELEMETRY_ENABLED", "true");
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddAetheusTelemetry(ValidConfiguration(("Aetheus:Telemetry:TraceSampleRatio", "1.1"))));

        Assert.Contains("TraceSampleRatio", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddAetheusTelemetry_DoesNotRequireLogsEndpointWhenLogsAreDisabled()
    {
        Environment.SetEnvironmentVariable("AETHEUS_TELEMETRY_ENABLED", "true");
        var services = new ServiceCollection();

        services.AddAetheusTelemetry(ValidConfiguration(
            ("Aetheus:Telemetry:LogsEndpoint", string.Empty),
            ("Aetheus:Telemetry:EnableLogs", "false")));

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType.FullName?.Contains("TracerProvider", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void AddAetheusTelemetry_Enabled_RegistersTheRequestRecorderAndItsGaugesOnce()
    {
        Environment.SetEnvironmentVariable("AETHEUS_TELEMETRY_ENABLED", "true");
        var services = new ServiceCollection();

        services.AddAetheusRequestPerformance(options => options.QuietRoutes.Add("api/report"));
        services.AddAetheusTelemetry(ValidConfiguration());

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(RequestPerformanceRecorder));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(RequestPerformanceMetrics));
        // Not resolving the hosted services keeps the OTLP providers unbuilt: nothing is exported here.
        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetRequiredService<RequestPerformanceMetrics>());
        // The host's configuration survives the package's own registration of the recorder.
        Assert.Contains("api/report", provider
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<RequestPerformanceOptions>>().Value.QuietRoutes);
    }

    [Fact]
    public void AddAetheusTelemetry_Disabled_MeasuresNoRequest()
    {
        Environment.SetEnvironmentVariable("AETHEUS_TELEMETRY_ENABLED", "false");
        var services = new ServiceCollection();

        services.AddAetheusTelemetry(ValidConfiguration());

        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(RequestPerformanceRecorder));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(RequestPerformanceMetrics));
    }

    public void Dispose() =>
        Environment.SetEnvironmentVariable("AETHEUS_TELEMETRY_ENABLED", _originalEnabled);

    private static IConfiguration ValidConfiguration(params (string Key, string Value)[] overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["Aetheus:Telemetry:Enabled"] = "true",
            ["Aetheus:Telemetry:ApplicationName"] = "test-app",
            ["Aetheus:Telemetry:ApplicationId"] = "42",
            ["Aetheus:Telemetry:EnvironmentName"] = "qa",
            ["Aetheus:Telemetry:MetricsEndpoint"] = "https://aetheus.example/api/ingest/otlp/v1/metrics",
            ["Aetheus:Telemetry:LogsEndpoint"] = "https://aetheus.example/api/ingest/otlp/v1/logs",
            ["Aetheus:Telemetry:TracesEndpoint"] = "https://aetheus.example/api/ingest/otlp/v1/traces",
            ["Aetheus:Telemetry:IngestKey"] = "secret",
            ["Aetheus:Telemetry:TraceSampleRatio"] = "0.25",
            ["Aetheus:Telemetry:MaxExportQueueSize"] = "1024"
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
