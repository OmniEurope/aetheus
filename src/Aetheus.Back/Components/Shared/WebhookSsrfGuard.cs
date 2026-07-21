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
        {
            var bytes = address.GetAddressBytes();
            if (bytes[0] == 10) return true;                                // 10.0.0.0/8
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true; // 172.16.0.0/12
            if (bytes[0] == 192 && bytes[1] == 168) return true;            // 192.168.0.0/16
            if (bytes[0] == 169 && bytes[1] == 254) return true;            // 169.254.0.0/16
            if (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127) return true; // 100.64.0.0/10 shared space
            if (bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 0) return true; // 192.0.0.0/24 IETF protocols
            if (bytes[0] == 198 && bytes[1] is 18 or 19) return true;        // 198.18.0.0/15 benchmarking
            if (bytes[0] == 0 || bytes[0] >= 224) return true;              // 0.0.0.0/8 + multicast
        }
        else if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast)
                return true;
            if (IPAddress.IPv6Any.Equals(address) || IPAddress.IPv6Loopback.Equals(address))
                return true;
            var bytes = address.GetAddressBytes();
            if ((bytes[0] & 0xfe) == 0xfc) return true;                     // fc00::/7
            if (address.IsIPv4MappedToIPv6)
                return IsForbiddenAddress(address.MapToIPv4());
        }

        return false;
    }
}
