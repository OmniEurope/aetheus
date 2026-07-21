// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Components.Environments;

public static class EnvironmentsModuleExtensions
{
    public static IServiceCollection AddEnvironmentsModule(this IServiceCollection services)
    {
        services.AddScoped<IEnvironmentRepository, EnvironmentRepository>();
        services.AddScoped<IEnvironmentService, EnvironmentService>();
        return services;
    }
}
