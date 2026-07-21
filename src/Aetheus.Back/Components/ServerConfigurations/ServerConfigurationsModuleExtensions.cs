// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Components.ServerConfigurations;

public static class ServerConfigurationsModuleExtensions
{
    public static IServiceCollection AddServerConfigurationsModule(this IServiceCollection services)
    {
        services.AddScoped<IServerConfigurationRepository, ServerConfigurationRepository>();
        services.AddScoped<IServerConfigurationService, ServerConfigurationService>();
        return services;
    }
}
