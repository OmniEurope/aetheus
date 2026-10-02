// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Configuration;

namespace Aetheus.Back.Components.ExternalRepos;

/// <summary>
/// DI registrations for the External-Git parity feature (mirror-backed external repositories).
/// Always on: recette R-295 removed the <c>Features:ExternalRepos</c> flag; a project uses an external
/// repository only when one is attached to it.
/// </summary>
public static class ExternalReposModuleExtensions
{
    public static IServiceCollection AddExternalReposModule(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IExternalRepoService, ExternalRepoService>();
        services.AddScoped<IRepoSourceResolver, RepoSourceResolver>();
        services.AddHostedService<ExternalRepoSyncService>();

        return services;
    }
}
