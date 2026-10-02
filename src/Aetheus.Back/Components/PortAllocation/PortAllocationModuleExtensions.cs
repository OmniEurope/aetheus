// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Components.PortAllocation;

public static class PortAllocationModuleExtensions
{
    public static IServiceCollection AddPortAllocationModule(this IServiceCollection services)
    {
        services.AddScoped<IPortAllocationRepository, PortAllocationRepository>();
        services.AddScoped<IPortAllocationService, PortAllocationService>();
        // PLAN-005 lot 6: records a server app's port. Registered here and not in ServerApps because
        // the registry sits on that module's own layer and may not be reached from it.
        services.AddScoped<
            Services.DomainEvents.IDomainEventHandler<ServerApps.Events.ServerAppPortChangedEvent>,
            ServerAppPortRegistryHandler>();
        return services;
    }
}
