// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using System.Net.Sockets;

namespace Aetheus.Back.Components.Shared;

/// <summary>
/// Shared SSRF guard for webhook outbound traffic. Used both at submission-time validation
/// (WebhookService) and at connect-time (SocketsHttpHandler.ConnectCallback) to defeat
/// DNS-rebinding TOCTOU between the validation and the actual TCP connect.
/// </summary>
public static class WebhookSsrfGuard
{
    public static bool IsForbiddenAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)) return true;

        if (address.AddressFamily == AddressFamily.InterNetwork)
            return IsForbiddenIpv4(address.GetAddressBytes());
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
            return IsForbiddenIpv6(address);

        return false;
    }

    private static bool IsForbiddenIpv4(byte[] bytes) =>
        bytes[0] == 10
        || bytes[0] == 172 && bytes[1] is >= 16 and <= 31
        || bytes[0] == 192 && bytes[1] == 168
        || bytes[0] == 169 && bytes[1] == 254
        || bytes[0] == 100 && bytes[1] is >= 64 and <= 127
        || bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 0
        || bytes[0] == 198 && bytes[1] is 18 or 19
        || bytes[0] is 0 or >= 224;

    private static bool IsForbiddenIpv6(IPAddress address)
    {
        if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast)
            return true;
        if (IPAddress.IPv6Any.Equals(address) || IPAddress.IPv6Loopback.Equals(address))
            return true;
        if ((address.GetAddressBytes()[0] & 0xfe) == 0xfc)
            return true;
        return address.IsIPv4MappedToIPv6 && IsForbiddenAddress(address.MapToIPv4());
    }
}
