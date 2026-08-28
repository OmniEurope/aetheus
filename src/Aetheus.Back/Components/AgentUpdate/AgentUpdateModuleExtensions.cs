// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Components.AgentUpdate;

public static class AgentUpdateModuleExtensions
{
    public static IServiceCollection AddAgentUpdateModule(this IServiceCollection services)
    {
        services.AddSingleton<AgentReleaseCatalog>();
        services.AddSingleton<IAgentReleaseCatalog>(serviceProvider =>
            serviceProvider.GetRequiredService<AgentReleaseCatalog>());
        services.AddHostedService(serviceProvider =>
            serviceProvider.GetRequiredService<AgentReleaseCatalog>());
        services.AddSingleton<IAgentCompatibilityPolicy, AgentCompatibilityPolicy>();
        services.AddScoped<IAgentUpdateRepository, AgentUpdateRepository>();
        services.AddScoped<IAgentUpdateConfirmationService, AgentUpdateConfirmationService>();
        services.AddScoped<IAgentUpdateService, AgentUpdateService>();
        services.AddHostedService<AgentUpdateCoordinatorService>();
        return services;
    }
}
