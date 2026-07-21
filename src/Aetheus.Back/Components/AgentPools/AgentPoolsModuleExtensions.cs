// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Components.AgentPools;

public static class AgentPoolsModuleExtensions
{
    public static IServiceCollection AddAgentPoolsModule(this IServiceCollection services)
    {
        services.AddScoped<IAgentPoolRepository, AgentPoolRepository>();
        services.AddScoped<IAgentPoolService, AgentPoolService>();
        return services;
    }
}
