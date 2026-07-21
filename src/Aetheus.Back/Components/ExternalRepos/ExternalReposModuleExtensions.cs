// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Configuration;

namespace Aetheus.Back.Components.ExternalRepos;

/// <summary>
/// DI registrations for the External-Git parity feature (mirror-backed external repositories).
/// Every service here is a no-op unless <c>Features:ExternalRepos</c> is enabled; the flag binding
/// lives here because this module owns the feature.
/// </summary>
public static class ExternalReposModuleExtensions
{
    public static IServiceCollection AddExternalReposModule(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<FeatureFlagsOptions>(configuration.GetSection(FeatureFlagsOptions.SectionName));

        services.AddSingleton<CreditedGitRunner>();
        services.AddScoped<IExternalRepoMirrorService, ExternalRepoMirrorService>();
        services.AddScoped<IExternalRepoService, ExternalRepoService>();
        services.AddScoped<IRepoSourceResolver, RepoSourceResolver>();
        services.AddHostedService<ExternalRepoSyncService>();

        return services;
    }
}
