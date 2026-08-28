// SPDX-License-Identifier: EUPL-1.2
using System.Globalization;

namespace Aetheus.Agent.Core.Operations;

internal static class ZapEgressConfigurator
{
    internal static void Configure(
        ScannerManifestEntry scanner,
        List<string> arguments,
        RestrictedScannerEgress? egress)
    {
        if (egress is null || !scanner.Key.StartsWith("zap-", StringComparison.OrdinalIgnoreCase))
            return;

        if (string.Equals(scanner.Key, "zap-active", StringComparison.OrdinalIgnoreCase))
        {
            ReplaceActiveScannerMarkers(arguments, egress);
            return;
        }

        var zapOptionsIndex = arguments.FindIndex(argument =>
            string.Equals(argument, "-z", StringComparison.Ordinal));
        if (zapOptionsIndex < 0 || zapOptionsIndex == arguments.Count - 1)
            throw new IOException("ZAP scanner manifest does not expose bounded runtime options.");

        arguments[zapOptionsIndex + 1] = string.Join(' ',
            arguments[zapOptionsIndex + 1],
            "-config connection.proxyChain.enabled=true",
            $"-config connection.proxyChain.hostName={egress.ProxyHost}",
            $"-config connection.proxyChain.port={egress.ProxyPort}",
            "-config connection.proxyChain.authEnabled=true",
            $"-config connection.proxyChain.userName={RestrictedScannerEgress.ProxyUserName}",
            $"-config connection.proxyChain.password={egress.ProxyPassword}");
    }

    private static void ReplaceActiveScannerMarkers(
        List<string> arguments,
        RestrictedScannerEgress egress)
    {
        var replacements = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["__AETHEUS_PROXY_HOST__"] = egress.ProxyHost,
            ["__AETHEUS_PROXY_PORT__"] = egress.ProxyPort.ToString(CultureInfo.InvariantCulture),
            ["__AETHEUS_PROXY_USERNAME__"] = RestrictedScannerEgress.ProxyUserName,
            ["__AETHEUS_PROXY_PASSWORD__"] = egress.ProxyPassword,
        };
        foreach (var replacement in replacements)
        {
            var index = arguments.FindIndex(argument =>
                string.Equals(argument, replacement.Key, StringComparison.Ordinal));
            if (index < 0)
                throw new IOException(
                    $"ZAP active scanner manifest is missing egress marker '{replacement.Key}'.");
            arguments[index] = replacement.Value;
        }
    }
}
