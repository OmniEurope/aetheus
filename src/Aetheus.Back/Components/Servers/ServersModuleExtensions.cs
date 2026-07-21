// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Services;

namespace Aetheus.Back.Components.Servers;

public static class ServersModuleExtensions
{
    public static IServiceCollection AddServersModule(this IServiceCollection services)
    {
        services.AddScoped<IServerRepository, ServerRepository>();
        services.AddScoped<ServerService>();
        // Segregated facets resolve to the same instance - consumers depend on the narrowest surface.
        services.AddScoped<IServerLifecycleService>(sp => sp.GetRequiredService<ServerService>());
        services.AddScoped<IServerHeartbeatService>(sp => sp.GetRequiredService<ServerService>());
        services.AddScoped<IServerServiceManagementService>(sp => sp.GetRequiredService<ServerService>());
        services.AddScoped<IServerAgentContactService>(sp => sp.GetRequiredService<ServerService>());
        services.AddScoped<IServerDiagnosticService>(sp => sp.GetRequiredService<ServerService>());
        services.AddHostedService<ServerTimeoutService>();
        return services;
    }
}
