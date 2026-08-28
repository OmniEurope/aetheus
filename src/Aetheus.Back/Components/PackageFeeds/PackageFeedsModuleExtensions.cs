// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Configuration;

namespace Aetheus.Back.Components.PackageFeeds;

public static class PackageFeedsModuleExtensions
{
    public static IServiceCollection AddPackageFeedsModule(this IServiceCollection services)
    {
        services.AddScoped<IPackageFeedRepository, PackageFeedRepository>();
        services.AddScoped<IPackageFeedService, PackageFeedService>();
        services.AddScoped<IPackageVersionResolver, PackageVersionResolver>();
        // Singleton so the per-feed sync lock is shared across the hosted-timer scope and controller scopes.
        services.AddSingleton<PackageFeedSyncGate>();

        // Registry-probe client: short timeout, SSRF ConnectCallback (a self-hosted UpstreamUrl can't
        // be used to reach internal services), no redirects/cookies.
        services.AddHttpClient("package-feeds", client =>
        {
            client.Timeout = BackendRuntimeDefaults.RegistryRequestTimeout;
            // Cap the buffered response: an UpstreamUrl is user-supplied, so a hostile/huge registry
            // reply must not be read unbounded into memory (OOM/DoS). 10 MB dwarfs any real index.json.
            client.MaxResponseContentBufferSize = 10 * 1024 * 1024;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Aetheus-PackageFeeds/1.0");
        }).ConfigurePrimaryHttpMessageHandler(sp =>
        {
            var cfg = sp.GetRequiredService<IConfiguration>();
            // Module-scoped SSRF escape hatch (defaults closed): relaxing package-feed targets must NOT
            // also open webhooks or service-connections. Each subsystem carries its own dedicated key.
            var allowPrivate = cfg.GetValue("PackageFeeds:AllowPrivateTargets", false);
            return SsrfProtectedHttpHandlerFactory.Create(
                allowPrivate, "Registry target resolved to a forbidden address.");
        });

        // Periodic upstream sync (opt-in via PackageFeeds:SyncIntervalMinutes > 0; off by default so we
        // don't hammer public registries in dev). Manual "sync now" always works regardless.
        services.AddHostedService<PackageFeedSyncHostedService>();

        return services;
    }
}
