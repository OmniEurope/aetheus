// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.PackageFeeds;

/// <summary>Outcome of resolving a package's latest version against its upstream registry.</summary>
public enum PackageResolveOutcome
{
    Resolved,
    NotFound,
    Unsupported,
    RateLimited,
    Error
}

public readonly record struct PackageResolveResult(
    PackageResolveOutcome Outcome,
    string? LatestVersion,
    DateTime? PublishedAt,
    TimeSpan? RetryAfter = null);

public interface IPackageVersionResolver
{
    /// <summary>
    /// Queries the real upstream registry for the latest version of <paramref name="packageName"/>.
    /// Registry types without an implementation return <see cref="PackageResolveOutcome.Unsupported"/> -
    /// they are never given a fabricated version.
    /// </summary>
    Task<PackageResolveResult> ResolveLatestAsync(PackageFeedType feedType, string upstreamUrl, string packageName, CancellationToken ct = default);
}
