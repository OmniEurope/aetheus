// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Components.Plugins;

public static class PluginsModuleExtensions
{
    public static IServiceCollection AddPluginsModule(this IServiceCollection services)
    {
        services.AddScoped<IPluginRepository, PluginRepository>();
        services.AddScoped<IPluginService, PluginService>();
        return services;
    }
}
