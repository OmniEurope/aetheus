// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Components.AgentUpdate;

public static class AgentUpdateModuleExtensions
{
    public static IServiceCollection AddAgentUpdateModule(this IServiceCollection services)
    {
        services.AddScoped<IAgentUpdateService, AgentUpdateService>();
        return services;
    }
}
