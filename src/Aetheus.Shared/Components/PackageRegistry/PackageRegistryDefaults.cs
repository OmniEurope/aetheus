// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Shared.Components.PackageRegistry;

/// <summary>Canonical public package-registry origins used by feed creation and resolution.</summary>
public static class PackageRegistryDefaults
{
    public const string NuGet = "https://api.nuget.org";
    public const string Npm = "https://registry.npmjs.org";
    public const string PyPi = "https://pypi.org";

    public static string For(PackageFeedType feedType) => feedType switch
    {
        PackageFeedType.NuGet => NuGet,
        PackageFeedType.Npm => Npm,
        PackageFeedType.PyPI => PyPi,
        _ => string.Empty
    };
}
