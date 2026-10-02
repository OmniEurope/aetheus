// SPDX-License-Identifier: EUPL-1.2
using System.Text.RegularExpressions;

namespace Aetheus.Shared.Components.Pipelines;

public static partial class ContainerExecutionContractValidator
{
    [GeneratedRegex(@"^[a-z][a-z0-9._-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex ToolchainPattern();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._+-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();

    [GeneratedRegex(@"^[^@\s]+@sha256:[a-fA-F0-9]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex ImmutableImagePattern();

    public static bool IsToolchainName(string? value) =>
        !string.IsNullOrWhiteSpace(value) && ToolchainPattern().IsMatch(value);

    public static bool IsExactVersion(string? value) =>
        !string.IsNullOrWhiteSpace(value) && VersionPattern().IsMatch(value);

    public static bool IsImmutableImage(string? value) =>
        !string.IsNullOrWhiteSpace(value) && ImmutableImagePattern().IsMatch(value);

    public static bool IsSupportedShell(string? value) =>
        value is null or "bash" or "sh";
}
