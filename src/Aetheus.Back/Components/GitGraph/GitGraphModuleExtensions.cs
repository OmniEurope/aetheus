// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Components.GitGraph;

public static class GitGraphModuleExtensions
{
    public static IServiceCollection AddGitGraphModule(this IServiceCollection services)
    {
        services.AddScoped<IGitGraphRepository, GitGraphRepository>();
        services.AddScoped<IGitGraphService, GitGraphService>();
        services.AddScoped<IGitGraphRecorder, GitGraphRecorder>();
        return services;
    }
}
