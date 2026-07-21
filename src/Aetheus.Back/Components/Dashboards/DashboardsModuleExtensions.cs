// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Components.Dashboards;

public static class DashboardsModuleExtensions
{
    public static IServiceCollection AddDashboardsModule(this IServiceCollection services)
    {
        services.AddScoped<IDashboardRepository, DashboardRepository>();
        services.AddScoped<IDashboardService, DashboardService>();
        return services;
    }
}
