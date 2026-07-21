// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Components.AgentInstaller;

public static class AgentInstallerModuleExtensions
{
    public static IServiceCollection AddAgentInstallerModule(this IServiceCollection services)
    {
        services.AddScoped<IAgentInstallerService, AgentInstallerService>();
        return services;
    }
}
