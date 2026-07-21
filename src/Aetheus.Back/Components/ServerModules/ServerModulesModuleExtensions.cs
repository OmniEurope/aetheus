// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Components.ServerModules;

public static class ServerModulesModuleExtensions
{
    public static IServiceCollection AddServerModulesModule(this IServiceCollection services)
    {
        services.AddScoped<IServerModuleRepository, ServerModuleRepository>();
        services.AddScoped<IServerModuleService, ServerModuleService>();
        return services;
    }
}
