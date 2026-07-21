// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Services;

namespace Aetheus.Back.Components.Monitoring;

public static class MonitoringModuleExtensions
{
    public static IServiceCollection AddMonitoringModule(this IServiceCollection services)
    {
        services.AddScoped<IMonitoringRepository, MonitoringRepository>();
        services.AddScoped<IMonitoringService, MonitoringService>();
        services.AddHostedService<MetricsCleanupService>();
        return services;
    }
}
