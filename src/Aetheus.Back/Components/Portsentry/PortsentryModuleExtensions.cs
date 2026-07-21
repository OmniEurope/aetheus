// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Components.Portsentry;

public static class PortsentryModuleExtensions
{
    public static IServiceCollection AddPortsentryModule(this IServiceCollection services)
    {
        services.AddScoped<IPortsentryRepository, PortsentryRepository>();
        services.AddScoped<IPortsentryService, PortsentryService>();
        return services;
    }
}
