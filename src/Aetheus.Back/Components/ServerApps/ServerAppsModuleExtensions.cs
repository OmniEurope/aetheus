// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Components.ServerApps;

public static class ServerAppsModuleExtensions
{
    public static IServiceCollection AddServerAppsModule(this IServiceCollection services)
    {
        services.AddScoped<IServerAppRepository, ServerAppRepository>();
        services.AddScoped<IServerAppService, ServerAppService>();
        return services;
    }
}
