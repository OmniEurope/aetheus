// SPDX-License-Identifier: EUPL-1.2
using Bunit;
using OmniEurope.Blazor.Components;

namespace Aetheus.Front.Tests.Shared;

/// <summary>
/// Recette R-536 (decision of 2026-10-01): AetheusLoader is the package's logo loader holding the Aetheus
/// plane. The float, the sizes and the reduced-motion stop are OE's; the wrapper gives the logo, the
/// words and the layout.
/// </summary>
public sealed class AetheusLoaderTests : BunitContext
{
    public AetheusLoaderTests() => BunitTestHelper.RegisterServices(this);

    [Fact]
    public void Default_RendersTheOeLogoLoaderWithThePlaneAndTheLoadingWords()
    {
        var cut = Render<AetheusLoader>();

        var loader = cut.FindComponent<OmniLogoLoader>();
        Assert.Equal(OmniControlSize.Medium, loader.Instance.Size);
        Assert.Equal("Loading", loader.Instance.Label);

        var root = cut.Find(".omni-logo-loader");
        Assert.Equal("status", root.GetAttribute("role"));
        Assert.Contains("aetheus-loader", root.ClassList);
        Assert.Contains("aetheus-loader-centered", root.ClassList);
        Assert.Contains("omni-logo-loader--medium", root.ClassList);

        // The logo is decorative inside OE's hidden mark; the state is said by the visually hidden words.
        var logo = cut.Find(".omni-logo-loader__mark > img.aetheus-loader-logo");
        Assert.Equal("aetheus-icon.svg", logo.GetAttribute("src"));
        Assert.Equal(string.Empty, logo.GetAttribute("alt"));
        Assert.Equal("true", cut.Find(".omni-logo-loader__mark").GetAttribute("aria-hidden"));
        Assert.Equal("Loading", cut.Find(".omni-logo-loader .omni-visually-hidden").TextContent);
        Assert.Empty(cut.FindAll("svg"));
    }

    [Theory]
    [InlineData(OmniControlSize.Small, "omni-logo-loader--small")]
    [InlineData(OmniControlSize.Large, "omni-logo-loader--large")]
    public void Size_IsOesSize(OmniControlSize size, string expectedClass)
    {
        var cut = Render<AetheusLoader>(parameters => parameters.Add(p => p.Size, size));

        Assert.Contains(expectedClass, cut.Find(".omni-logo-loader").ClassList);
    }

    [Fact]
    public void NotCentered_StaysInlineAndKeepsTheCallersClass()
    {
        var cut = Render<AetheusLoader>(parameters => parameters
            .Add(p => p.Centered, false)
            .Add(p => p.Class, "omni-u-mb-sm"));

        var root = cut.Find(".omni-logo-loader");
        Assert.DoesNotContain("aetheus-loader-centered", root.ClassList);
        Assert.Contains("omni-u-mb-sm", root.ClassList);
    }
}
