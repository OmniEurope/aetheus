// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Components.Cron;

public static class CronModuleExtensions
{
    public static IServiceCollection AddCronModule(this IServiceCollection services)
    {
        services.AddScoped<ICronRepository, CronRepository>();
        services.AddScoped<ICronService, CronService>();
        return services;
    }
}
