// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Configuration;

/// <summary>
/// Strongly typed binding for the <c>Features</c> configuration section.
/// Opt-in feature flags that gate not-yet-general-availability code paths.
/// </summary>
public sealed class FeatureFlagsOptions
{
    public const string SectionName = "Features";

    /// <summary>
    /// External-Git parity (mirror-backed external repositories). OFF by default -
    /// every external-repo code path is gated behind this flag until activation day.
    /// </summary>
    public bool ExternalRepos { get; set; }
}
