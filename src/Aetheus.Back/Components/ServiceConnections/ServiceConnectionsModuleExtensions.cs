// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using System.Net.Sockets;
using Aetheus.Back.Components.Shared;
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

            return new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false,
                ConnectCallback = async (ctx, ct) =>
                {
                    var addresses = await Dns.GetHostAddressesAsync(ctx.DnsEndPoint.Host, ct).ConfigureAwait(false);
                    var safe = addresses.FirstOrDefault(a => allowPrivate || !WebhookSsrfGuard.IsForbiddenAddress(a))
                        ?? throw new HttpRequestException("Provider target resolved to a forbidden address.");

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
