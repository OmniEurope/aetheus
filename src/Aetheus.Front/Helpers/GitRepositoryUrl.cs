// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Helpers;

public static class GitRepositoryUrl
{
    public static string? Canonicalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var canonical = value.Trim().Replace('\\', '/').TrimEnd('/');
        if (canonical.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) canonical = canonical[..^4];
        return canonical.ToLowerInvariant();
    }

    public static string? CanonicalPath(string? value)
    {
        var canonical = Canonicalize(value);
        if (canonical is null) return null;
        if (Uri.TryCreate(canonical, UriKind.Absolute, out var uri))
            return uri.AbsolutePath.TrimEnd('/').ToLowerInvariant();
        return canonical.StartsWith('/') ? canonical : null;
    }
}
