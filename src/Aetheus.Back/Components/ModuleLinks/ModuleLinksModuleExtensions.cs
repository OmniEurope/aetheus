// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Components.ModuleLinks;

public static class ModuleLinksModuleExtensions
{
    public static IServiceCollection AddModuleLinksModule(this IServiceCollection services)
    {
        services.AddScoped<IModuleLinkRepository, ModuleLinkRepository>();
        services.AddScoped<IModuleLinkService, ModuleLinkService>();
        return services;
    }
}
