// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Components.Rkhunter;

public static class RkhunterModuleExtensions
{
    public static IServiceCollection AddRkhunterModule(this IServiceCollection services)
    {
        services.AddScoped<IRkhunterRepository, RkhunterRepository>();
        services.AddScoped<IRkhunterService, RkhunterService>();
        services.AddHostedService<RkhunterSchedulerService>();
        return services;
    }
}
