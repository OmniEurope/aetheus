// SPDX-License-Identifier: EUPL-1.2
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Aetheus.Telemetry;

public static class AetheusRequestPerformanceExtensions
{
    /// <summary>
    /// Registers <see cref="RequestPerformanceRecorder"/>, started with the host so it hears the first
    /// request. Safe to call more than once: the recorder is registered once and every
    /// <paramref name="configure"/> is applied. <c>AddAetheusTelemetry</c> calls it when the export is
    /// enabled; a host that reads the figures itself calls it whether or not the export is on.
    /// </summary>
    public static IServiceCollection AddAetheusRequestPerformance(
        this IServiceCollection services,
        Action<RequestPerformanceOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddOptions<RequestPerformanceOptions>();
        if (configure is not null)
            services.Configure(configure);

        if (services.Any(descriptor => descriptor.ServiceType == typeof(RequestPerformanceRecorder)))
            return services;

        services.AddSingleton(provider => new RequestPerformanceRecorder(
            provider.GetRequiredService<IOptions<RequestPerformanceOptions>>(),
            provider.GetService<TimeProvider>() ?? TimeProvider.System));
        services.AddHostedService(provider => provider.GetRequiredService<RequestPerformanceRecorder>());
        return services;
    }

    /// <summary>Adds the per-route gauges of <see cref="RequestPerformanceMetrics"/> once.</summary>
    internal static IServiceCollection AddAetheusRequestPerformanceMetrics(this IServiceCollection services)
    {
        services.AddAetheusRequestPerformance();
        if (services.Any(descriptor => descriptor.ServiceType == typeof(RequestPerformanceMetrics)))
            return services;

        services.TryAddSingleton(provider => new RequestPerformanceMetrics(
            provider.GetRequiredService<RequestPerformanceRecorder>(),
            provider.GetService<TimeProvider>() ?? TimeProvider.System));
        services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<RequestPerformanceMetrics>());
        return services;
    }
}
