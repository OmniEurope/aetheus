// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Components.VariableLibraries;

public static class VariableLibrariesModuleExtensions
{
    public static IServiceCollection AddVariableLibrariesModule(this IServiceCollection services)
    {
        services.AddScoped<IVariableLibraryRepository, VariableLibraryRepository>();
        services.AddScoped<IVariableLibraryService, VariableLibraryService>();
        return services;
    }
}
