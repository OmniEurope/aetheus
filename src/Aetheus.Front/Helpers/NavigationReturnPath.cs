// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Front.Helpers;

internal static class NavigationReturnPath
{
    internal static string Resolve(string? requestedPath, string fallback)
    {
        if (string.IsNullOrWhiteSpace(requestedPath)
            || requestedPath[0] != '/'
            || (requestedPath.Length > 1 && (requestedPath[1] is '/' or '\\'))
            || requestedPath.Contains('\\', StringComparison.Ordinal)
            || requestedPath.Any(char.IsControl)
            || !Uri.TryCreate(requestedPath, UriKind.Relative, out _))
            return fallback;

        return requestedPath;
    }

    internal static string AddTo(string targetPath, string returnPath) =>
        $"{targetPath}?from={Uri.EscapeDataString(returnPath)}";
}
