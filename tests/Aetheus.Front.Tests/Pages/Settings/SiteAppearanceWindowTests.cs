// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Components.Settings;
using Aetheus.Front.Components.Shared;
using Bunit;

namespace Aetheus.Front.Tests.Pages.Settings;

/// <summary>
/// Recette R-390: the user menu's "Theme" opens OE's appearance window with the site's theme, palette,
/// text size, density and control size, read from and written to the same storage keys as the settings
/// page (SiteAppearanceState), each applied on the document root at once.
/// </summary>
public sealed class SiteAppearanceWindowTests : BunitContext
{
    public SiteAppearanceWindowTests() => BunitTestHelper.RegisterServices(this);

    [Theory]
    [InlineData(OmniDensity.Compact, "compact")]
    [InlineData(OmniDensity.Comfortable, "comfortable")]
    [InlineData(OmniDensity.Spacious, "spacious")]
    public void Densities_AreStoredUnderTheNamesTheStylesheetKnows(OmniDensity density, string stored)
    {
        Assert.Equal(stored, SiteAppearance.StoredDensity(density));
        Assert.Equal(density, SiteAppearance.ParseDensity(stored));
    }

    [Fact]
    public async Task Open_ReadsTheStoredChoices_AndShowsEveryRowItApplies()
    {
        var theme = OmniThemePresets.All[1];
        JSInterop.Setup<string?>("localStorage.getItem", StorageKeys.ThemePreset).SetResult(theme.Name);
        JSInterop.Setup<string?>("localStorage.getItem", StorageKeys.ThemePalette).SetResult(string.Empty);
        JSInterop.Setup<string?>("localStorage.getItem", StorageKeys.Density).SetResult("compact");
        JSInterop.Setup<string?>("localStorage.getItem", StorageKeys.TextSize).SetResult("7");
        JSInterop.Setup<string?>("localStorage.getItem", StorageKeys.ControlSize).SetResult("3");
        var cut = Render<SiteAppearanceWindow>();
        Assert.Empty(cut.FindAll(".omni-appearance-window"));

        await cut.InvokeAsync(cut.Instance.OpenAsync);

        var window = cut.Find(".omni-appearance-window");
        // OE 1.3.0: one block of rows in reading order, theme, palette, text size, control size, density.
        var rows = window.QuerySelectorAll(".omni-appearance-settings--window .omni-appearance-settings__row");
        Assert.Equal(5, rows.Length);
        Assert.Contains("7/10", rows[2].TextContent, StringComparison.Ordinal);
        Assert.Contains("3/10", rows[3].TextContent, StringComparison.Ordinal);
        var densities = rows[4].QuerySelectorAll(".omni-select-bar__item");
        Assert.Equal(3, densities.Length);
        Assert.Contains("omni-select-bar__item--selected", densities[0].ClassList);
    }

    /// <summary>User decision 2026-09-29: no "Aetheus" theme of its own; the list is OE's, where the
    /// first catalogue theme (Essentiel) stands for the default.</summary>
    [Fact]
    public async Task TheThemeList_IsOesOwn_WithoutAnAetheusEntry()
    {
        var cut = Render<SiteAppearanceWindow>();
        await cut.InvokeAsync(cut.Instance.OpenAsync);

        var themes = cut.Find(".omni-appearance-window select").QuerySelectorAll("option").Select(option => option.TextContent).ToList();
        Assert.StartsWith(OmniThemePresets.All[0].Name, themes[0], StringComparison.Ordinal);
        Assert.Equal(OmniThemePresets.All.Count, themes.Count);
        Assert.DoesNotContain("Aetheus", themes);
    }

    [Fact]
    public async Task ChangingTheDensity_StoresAndAppliesItsBand()
    {
        var cut = Render<SiteAppearanceWindow>();
        await cut.InvokeAsync(cut.Instance.OpenAsync);

        // OE shows the three densities as a bar of choices: the third is Spacious.
        cut.FindAll(".omni-appearance-settings--window .omni-select-bar__item")[2].Click();

        Assert.Contains(JSInterop.Invocations, call => call.Identifier == "localStorage.setItem"
            && Equals(call.Arguments[0], StorageKeys.Density) && Equals(call.Arguments[1], "spacious"));
        Assert.Contains(JSInterop.Invocations, call => call.Identifier == "Aetheus.setDensity"
            && Equals(call.Arguments[0], "spacious"));
    }

    [Fact]
    public async Task ChangingTheTextAndControlSizes_StoresAndAppliesThem()
    {
        var cut = Render<SiteAppearanceWindow>();
        await cut.InvokeAsync(cut.Instance.OpenAsync);
        var sliders = cut.FindAll(".omni-appearance-settings--window input[type=range]");

        sliders[0].Input("8");
        cut.FindAll(".omni-appearance-settings--window input[type=range]")[1].Input("2");

        Assert.Contains(JSInterop.Invocations, call => call.Identifier == "localStorage.setItem"
            && Equals(call.Arguments[0], StorageKeys.TextSize) && Equals(call.Arguments[1], "8"));
        Assert.Contains(JSInterop.Invocations, call => call.Identifier == "Aetheus.setTextSize"
            && Equals(call.Arguments[0], 8));
        Assert.Contains(JSInterop.Invocations, call => call.Identifier == "localStorage.setItem"
            && Equals(call.Arguments[0], StorageKeys.ControlSize) && Equals(call.Arguments[1], "2"));
        Assert.Contains(JSInterop.Invocations, call => call.Identifier == "Aetheus.setControlSize"
            && Equals(call.Arguments[0], 2));
    }

    [Theory]
    [InlineData(null, 5)]
    [InlineData("", 5)]
    [InlineData("0", 5)]
    [InlineData("11", 5)]
    [InlineData("-3", 5)]
    [InlineData("x", 5)]
    [InlineData("1", 1)]
    [InlineData("10", 10)]
    public void AStoredScaleLevel_OutOfRange_ReadsAsDrawn(string? stored, int expected)
        => Assert.Equal(expected, SiteAppearance.ParseLevel(stored));
}
