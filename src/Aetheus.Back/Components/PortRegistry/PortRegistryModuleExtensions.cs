// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Components.PortRegistry;

public static class PortRegistryModuleExtensions
{
    public static IServiceCollection AddPortRegistryModule(this IServiceCollection services)
    {
        services.AddScoped<IPortRegistryRepository, PortRegistryRepository>();
        services.AddScoped<IPortRegistryService, PortRegistryService>();
        return services;
    }
}
