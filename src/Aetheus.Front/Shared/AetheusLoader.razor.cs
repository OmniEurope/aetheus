// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Shared;

public enum AetheusLoaderSize
{
    ExtraSmall,
    Small,
    Medium,
    Large
}

public partial class AetheusLoader
{
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    [Parameter] public AetheusLoaderSize Size { get; set; } = AetheusLoaderSize.Medium;
    [Parameter] public bool Centered { get; set; } = true;
    [Parameter] public string? Class { get; set; }

    private string CssClass => string.Join(' ', new[]
    {
        "aetheus-loader",
        $"aetheus-loader-{Size.ToString().ToLowerInvariant()}",
        Centered ? "aetheus-loader-centered" : null,
        Class
    }.Where(value => !string.IsNullOrWhiteSpace(value)));
}
