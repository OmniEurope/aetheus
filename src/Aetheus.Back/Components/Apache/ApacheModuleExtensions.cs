// SPDX-License-Identifier: EUPL-1.2
using Microsoft.Extensions.Caching.Memory;

namespace Aetheus.Back.Components.Apache;

public static class ApacheModuleExtensions
{
    public static IServiceCollection AddApacheModule(this IServiceCollection services)
    {
        services.AddScoped<IApacheRepository, ApacheRepository>();
        services.AddKeyedScoped<IApacheService, ApacheService>("inner");
        services.AddScoped<IApacheService>(sp =>
            new CachedApacheService(
                sp.GetRequiredKeyedService<IApacheService>("inner"),
                sp.GetRequiredService<IMemoryCache>()));
        return services;
    }
}
