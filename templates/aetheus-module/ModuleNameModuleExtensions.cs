// SPDX-License-Identifier: EUPL-1.2
using Microsoft.Extensions.DependencyInjection;

namespace Aetheus.Back.Components.ModuleName;

public static class ModuleNameModuleExtensions
{
    public static IServiceCollection AddModuleNameModule(this IServiceCollection services)
    {
        services.AddScoped<IModuleNameRepository, ModuleNameRepository>();
        services.AddScoped<IModuleNameService, ModuleNameService>();
        return services;
    }
}
