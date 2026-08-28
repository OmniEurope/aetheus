// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Configuration;

namespace Aetheus.Back.Components.ServiceConnections;

public static class ServiceConnectionsModuleExtensions
{
    public static IServiceCollection AddServiceConnectionsModule(this IServiceCollection services)
    {
        services.AddScoped<IServiceConnectionRepository, ServiceConnectionRepository>();
        services.AddScoped<IServiceConnectionService, ServiceConnectionService>();
        services.AddScoped<IServiceConnectionTester, ServiceConnectionTester>();

        // Provider-probe client: short timeout, no redirects/cookies, and an SSRF ConnectCallback
        // that rejects loopback/private/link-local targets at connect time (same guard as webhooks),
        // so a connection Url can never be used to reach internal services.
        services.AddHttpClient("service-connection-test", client =>
        {
            client.Timeout = BackendRuntimeDefaults.OutboundRequestTimeout;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Aetheus-ServiceConnectionTest/1.0");
        }).ConfigurePrimaryHttpMessageHandler(sp =>
        {
            var cfg = sp.GetRequiredService<IConfiguration>();
            // Module-scoped SSRF escape hatch (defaults closed): relaxing service-connection probe targets
            // must NOT also open webhooks or package-feeds. Each subsystem carries its own dedicated key.
            var allowPrivate = cfg.GetValue("ServiceConnections:AllowPrivateTargets", false);

            return SsrfProtectedHttpHandlerFactory.Create(
                allowPrivate, "Provider target resolved to a forbidden address.");
        });

        return services;
    }
}
