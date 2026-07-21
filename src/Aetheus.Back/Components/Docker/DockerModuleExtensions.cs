// SPDX-License-Identifier: EUPL-1.2
using Microsoft.Extensions.Caching.Memory;

namespace Aetheus.Back.Components.Docker;

public static class DockerModuleExtensions
{
    public static IServiceCollection AddDockerModule(this IServiceCollection services)
    {
        services.AddScoped<IDockerRepository, DockerRepository>();
        services.AddKeyedScoped<IDockerService, DockerService>("inner");
        services.AddScoped<IDockerService>(sp =>
            new CachedDockerService(
                sp.GetRequiredKeyedService<IDockerService>("inner"),
                sp.GetRequiredService<IMemoryCache>()));
        return services;
    }
}
