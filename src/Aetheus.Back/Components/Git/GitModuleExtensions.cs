// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.Git;

public static class GitModuleExtensions
{
    public static IServiceCollection AddGitModule(this IServiceCollection services, IConfiguration configuration)
    {
        // Existing Git connections
        services.AddScoped<IGitRepository, GitRepository>();
        services.AddScoped<IGitService, GitService>();

        // Git Light (internal repos)
        services.Configure<GitLightOptions>(configuration.GetSection("GitLight"));
        services.AddScoped<IGitLightRepository, GitLightRepository>();
        services.AddScoped<GitProcessRunner>();
        services.AddScoped<GitLightCliWriter>();
        services.AddScoped<IGitLightCliService, GitLightCliService>();
        services.AddScoped<GitBranchProtectionService>();
        services.AddScoped<GitAiPatchService>();
        services.AddScoped<IGitLightService, GitLightService>();
        services.AddScoped<IGitSmartHttpService, GitSmartHttpService>();
        services.AddHostedService<GitLightMaintenanceService>();

        return services;
    }
}
