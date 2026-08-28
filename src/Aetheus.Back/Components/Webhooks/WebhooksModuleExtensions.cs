// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Configuration;

namespace Aetheus.Back.Components.Webhooks;

public static class WebhooksModuleExtensions
{
    public static IServiceCollection AddWebhooksModule(this IServiceCollection services)
    {
        services.AddScoped<IWebhookRepository, WebhookRepository>();
        services.AddScoped<IWebhookService, WebhookService>();

        // Hardened client for outbound webhooks: short timeout, no auto-redirect (prevents
        // open-redirect-driven SSRF bypass), no cookies, fixed UA.
        // Hardening (#16): use SocketsHttpHandler.ConnectCallback to validate the resolved IP
        // at connect time, mitigating DNS-rebinding TOCTOU between IsTargetUrlSafeAsync and
        // the actual SendAsync. The callback rejects loopback/private/link-local/multicast.
        services.AddHttpClient("webhooks", client =>
        {
            client.Timeout = BackendRuntimeDefaults.OutboundRequestTimeout;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Aetheus-Webhooks/1.0");
        }).ConfigurePrimaryHttpMessageHandler(sp =>
        {
            var cfg = sp.GetRequiredService<IConfiguration>();
            var allowPrivate = cfg.GetValue("Webhooks:AllowPrivateTargets", false);

            return SsrfProtectedHttpHandlerFactory.Create(
                allowPrivate, "Webhook target resolved to a forbidden address.");
        });

        return services;
    }
}
