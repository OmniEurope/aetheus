// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Components.Artifacts;

public static class ArtifactsModuleExtensions
{
    public static IServiceCollection AddArtifactsModule(this IServiceCollection services)
    {
        services.AddScoped<IArtifactRepository, ArtifactRepository>();
        services.AddScoped<IArtifactService, ArtifactService>();
        services.AddScoped<IArtifactRetentionService, ArtifactRetentionService>();
        services.AddSingleton<IArtifactStorageService, ArtifactStorageService>();
        services.AddHostedService<ArtifactCleanupService>();
        services.AddHostedService<ArtifactStorageMonitorService>();
        return services;
    }
}
