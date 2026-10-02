// SPDX-License-Identifier: EUPL-1.2
using System.Globalization;

namespace Aetheus.Agent.Core.Collectors;

/// <summary>
/// Reads how each certbot lineage renews and compares it with the Aetheus convention (PLAN-007): the
/// ACME web root the installer writes into <c>/etc/letsencrypt/cli.ini</c>. Also reads the outcome of
/// the last renewal rehearsal the <c>aetheus-certbot-manage renewal-check</c> helper recorded. Files
/// are read directly (the certbot introspection ACL covers <c>/etc/letsencrypt</c>); an unreadable file
/// yields <see cref="CertbotRenewalConvention.Unknown"/>, never a conforming verdict.
/// </summary>
internal sealed class CertbotRenewalInspector(Func<string, string?> readFile, Func<string, bool> fileExists)
{
    private const string CliIniPath = "/etc/letsencrypt/cli.ini";
    private const string RenewalDir = "/etc/letsencrypt/renewal";
    private const string AcmeAliasPath = "/etc/apache2/conf-enabled/aetheus-acme-webroot.conf";
    private const string RenewalCheckPath = "/var/lib/aetheus-certbot/renewal-check";

    public List<CertbotCertificateDto> Annotate(IEnumerable<CertbotCertificateDto> certificates)
    {
        var expectedWebroot = ParseCliIniWebroot(readFile(CliIniPath));
        var aliasEnabled = fileExists(AcmeAliasPath);
        return certificates.Select(certificate =>
        {
            var renewal = ParseRenewalFile(readFile($"{RenewalDir}/{certificate.Name}.conf"));
            return certificate with
            {
                Authenticator = renewal?.Authenticator ?? string.Empty,
                WebrootPath = renewal is null ? string.Empty : string.Join(", ", renewal.Webroots),
                RenewalConvention = Evaluate(renewal, expectedWebroot, aliasEnabled)
            };
        }).ToList();
    }

    public (DateTime? CheckedAt, bool? Succeeded) ReadRenewalCheck() => ParseRenewalCheck(readFile(RenewalCheckPath));

    internal static CertbotRenewalConvention Evaluate(RenewalSettings? renewal, string? expectedWebroot, bool aliasEnabled)
    {
        if (renewal is null || expectedWebroot is null || renewal.Authenticator.Length == 0)
            return CertbotRenewalConvention.Unknown;
        if (renewal.Authenticator == "webroot")
            return renewal.Webroots.Count == 1 && renewal.Webroots[0] == expectedWebroot
                ? CertbotRenewalConvention.Conforming
                : CertbotRenewalConvention.WrongWebroot;
        // A server without the Aetheus ACME alias serves no web root: standalone is its only way to renew.
        return renewal.Authenticator == "standalone" && !aliasEnabled
            ? CertbotRenewalConvention.Conforming
            : CertbotRenewalConvention.NotWebroot;
    }

    /// <summary>
    /// Mirrors the manage helper's parsing: <c>authenticator</c> plus every web root the file names, from
    /// <c>webroot_path</c> (a comma list) and each <c>[[webroot_map]]</c> entry.
    /// </summary>
    internal static RenewalSettings? ParseRenewalFile(string? content)
    {
        if (content is null) return null;
        var authenticator = string.Empty;
        var webroots = new SortedSet<string>(StringComparer.Ordinal);
        var inWebrootMap = false;
        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.StartsWith('['))
            {
                inWebrootMap = line == "[[webroot_map]]";
                continue;
            }
            var separator = line.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0) continue;
            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (key == "authenticator" && authenticator.Length == 0)
                authenticator = value;
            else if (key == "webroot_path" || inWebrootMap)
                foreach (var path in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    webroots.Add(path);
        }
        return new RenewalSettings(authenticator, [.. webroots]);
    }

    internal static string? ParseCliIniWebroot(string? content)
    {
        if (content is null) return null;
        string? webroot = null;
        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim();
            var separator = line.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0 || line.StartsWith('#')) continue;
            if (line[..separator].Trim() is "webroot-path" or "webroot_path")
                webroot = line[(separator + 1)..].Trim(); // certbot keeps the last occurrence
        }
        return string.IsNullOrEmpty(webroot) ? null : webroot;
    }

    internal static (DateTime? CheckedAt, bool? Succeeded) ParseRenewalCheck(string? content)
    {
        if (content is null) return (null, null);
        long? checkedAt = null;
        int? exitCode = null;
        foreach (var rawLine in content.Split('\n'))
        {
            var parts = rawLine.Trim().Split('=', 2);
            if (parts.Length != 2) continue;
            if (parts[0] == "checked_at" && long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var epoch))
                checkedAt = epoch;
            else if (parts[0] == "exit_code" && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var code))
                exitCode = code;
        }
        return checkedAt is null || exitCode is null
            ? (null, null)
            : (DateTimeOffset.FromUnixTimeSeconds(checkedAt.Value).UtcDateTime, exitCode == 0);
    }

    internal sealed record RenewalSettings(string Authenticator, IReadOnlyList<string> Webroots);
}
