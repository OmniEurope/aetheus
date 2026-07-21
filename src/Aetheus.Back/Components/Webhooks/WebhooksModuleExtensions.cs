// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using System.Net.Sockets;
using Aetheus.Back.Components.Shared;
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

            return new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false,
                ConnectCallback = async (ctx, ct) =>
                {
                    var addresses = await Dns.GetHostAddressesAsync(ctx.DnsEndPoint.Host, ct).ConfigureAwait(false);
                    var safe = addresses.FirstOrDefault(a => allowPrivate || !WebhookSsrfGuard.IsForbiddenAddress(a))
                        ?? throw new HttpRequestException("Webhook target resolved to a forbidden address.");

                    var socket = new Socket(safe.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                    try
                    {
                        await socket.ConnectAsync(safe, ctx.DnsEndPoint.Port, ct).ConfigureAwait(false);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch
                    {
                        socket.Dispose();
                        throw;
                    }
                }
            };
        });

        return services;
    }
}
