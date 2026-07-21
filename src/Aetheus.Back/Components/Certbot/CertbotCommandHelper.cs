// SPDX-License-Identifier: EUPL-1.2
using System.Text.RegularExpressions;

namespace Aetheus.Back.Components.Certbot;

public static partial class CertbotCommandHelper
{
    public static bool IsValidCertName(string name) =>
        !string.IsNullOrWhiteSpace(name) && CertNameRegex().IsMatch(name);

    public static bool IsValidDomainList(string domains) =>
        !string.IsNullOrWhiteSpace(domains) &&
        domains.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
               .All(d => DomainRegex().IsMatch(d));

    public static bool IsValidEmail(string email) =>
        !string.IsNullOrWhiteSpace(email) && EmailRegex().IsMatch(email);

    public static bool IsValidWebrootPath(string path) =>
        !string.IsNullOrWhiteSpace(path) && WebrootPathRegex().IsMatch(path);

    // Alphanumeric, hyphens, dots (valid domain/cert name)
    [GeneratedRegex(@"^[a-zA-Z0-9][a-zA-Z0-9._-]{0,199}$")]
    private static partial Regex CertNameRegex();

    // Valid domain name
    [GeneratedRegex(@"^(\*\.)?[a-zA-Z0-9]([a-zA-Z0-9-]*[a-zA-Z0-9])?(\.[a-zA-Z0-9]([a-zA-Z0-9-]*[a-zA-Z0-9])?)*\.[a-zA-Z]{2,}$")]
    private static partial Regex DomainRegex();

    [GeneratedRegex(@"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$")]
    private static partial Regex EmailRegex();

    // Absolute POSIX path \u2014 alphanumerics, dashes, underscores, dots, slashes only.
    // Rejects anything that could break out of the -w argument (spaces, $, `, ;, &, |, etc.).
    [GeneratedRegex(@"^/[a-zA-Z0-9._\-/]{0,255}$")]
    private static partial Regex WebrootPathRegex();
}
