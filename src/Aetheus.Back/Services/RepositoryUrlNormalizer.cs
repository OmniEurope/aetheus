// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Services;

public static class RepositoryUrlNormalizer
{
    /// <summary>
    /// Normalizes a Git repository URL for case-insensitive equality comparison: strips trailing
    /// slashes and the optional <c>.git</c> suffix.
    /// </summary>
    public static string Normalize(string url)
    {
        var trimmed = url.TrimEnd('/');
        return trimmed.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? trimmed[..^4] : trimmed;
    }
}
