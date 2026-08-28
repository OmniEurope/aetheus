// SPDX-License-Identifier: EUPL-1.2
using System.Net;

namespace Aetheus.WebAnalytics;

internal static class IpNetworkMatcher
{
    internal static bool Contains(string network, IPAddress address)
    {
        if (!TryParse(network, out var networkAddress, out var prefixLength))
            return false;

        address = Normalize(address);
        networkAddress = Normalize(networkAddress!);
        if (address.AddressFamily != networkAddress.AddressFamily)
            return false;

        var candidate = address.GetAddressBytes();
        var expected = networkAddress.GetAddressBytes();
        var wholeBytes = prefixLength / 8;
        var remainingBits = prefixLength % 8;
        for (var index = 0; index < wholeBytes; index++)
        {
            if (candidate[index] != expected[index]) return false;
        }
        if (remainingBits == 0) return true;

        var mask = (byte)(0xFF << (8 - remainingBits));
        return (candidate[wholeBytes] & mask) == (expected[wholeBytes] & mask);
    }

    internal static bool IsValid(string network) =>
        TryParse(network, out _, out _);

    private static bool TryParse(
        string value,
        out IPAddress? address,
        out int prefixLength)
    {
        address = null;
        var parts = value.Split('/', StringSplitOptions.TrimEntries);
        if (parts.Length is < 1 or > 2 || !IPAddress.TryParse(parts[0], out address))
        {
            prefixLength = 0;
            return false;
        }

        address = Normalize(address);
        var maximum = address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
            ? 32
            : 128;
        if (parts.Length == 1)
        {
            prefixLength = maximum;
            return true;
        }
        return int.TryParse(parts[1], out prefixLength)
            && prefixLength >= 0
            && prefixLength <= maximum;
    }

    private static IPAddress Normalize(IPAddress address) =>
        address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
}
