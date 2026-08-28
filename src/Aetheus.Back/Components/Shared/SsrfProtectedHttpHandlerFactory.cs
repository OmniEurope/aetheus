// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using System.Net.Sockets;

namespace Aetheus.Back.Components.Shared;

public static class SsrfProtectedHttpHandlerFactory
{
    public static SocketsHttpHandler Create(bool allowPrivate, string forbiddenTargetMessage) => new()
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        ConnectCallback = async (context, ct) =>
        {
            var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct).ConfigureAwait(false);
            var safe = addresses.FirstOrDefault(address =>
                allowPrivate || !WebhookSsrfGuard.IsForbiddenAddress(address))
                ?? throw new HttpRequestException(forbiddenTargetMessage);
            var socket = new Socket(safe.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(safe, context.DnsEndPoint.Port, ct).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
    };
}
