// SPDX-License-Identifier: EUPL-1.2
using Bunit;
using SettingsPage = Aetheus.Front.Components.Settings.Settings;

namespace Aetheus.Front.Tests.Pages;

public class SettingsTests : BunitContext
{
    public SettingsTests()
    {
        _handler = BunitTestHelper.RegisterServices(this, isAdmin: false);
        _handler.SetJsonResponse(
            HttpMethod.Get,
            "api/users/me",
            new Aetheus.Shared.Components.Users.UserDto
            {
                Id = 1,
                Username = "testuser",
                IsActive = true,
                Roles = ["User"]
            });
    }

    private readonly BunitTestHelper.TestHandler _handler;

    [Fact]
    public void Renders_Settings_Page()
    {
        var cut = Render<SettingsPage>();
        cut.WaitForState(() => cut.Markup.Length > 50, TimeSpan.FromSeconds(2));

        Assert.Contains("Settings", cut.Markup);
    }

    [Fact]
    public void Renders_ForNonAdmin()
    {
        var cut = Render<SettingsPage>();
        cut.WaitForState(() => cut.Markup.Length > 50, TimeSpan.FromSeconds(2));

        // Settings is not admin-gated - a non-admin user still gets the full settings page.
        Assert.Contains("Settings", cut.Markup);
    }

    [Fact]
    public async Task AppearanceAndLanguage_PersistImmediately()
    {
        var cut = Render<SettingsPage>(p => p.Add(x => x.Tab, "appearance"));
        cut.WaitForState(() => cut.Markup.Length > 50, TimeSpan.FromSeconds(2));

        // Recette R-448: the mode is OE's appearance settings (Light, Dark, System); the language stays
        // a dropdown in its own section.
        var settings = cut.FindComponent<OmniAppearanceSettings>();
        var language = cut.FindComponents<OmniDropDown<string>>()
            .Single(selector => selector.Instance.Id == "oe-formfield-pages-settings-settings-3");

        await cut.InvokeAsync(() => settings.Instance.AppearanceChanged.InvokeAsync(OmniAppearance.Light));
        await cut.InvokeAsync(() => language.Instance.ValueChanged.InvokeAsync("fr-FR"));

        var keys = JSInterop.Invocations
            .Where(i => i.Identifier == "localStorage.setItem")
            .Select(i => (string?)i.Arguments[0])
            .ToList();
        Assert.Contains("aetheus_theme", keys);
        Assert.Contains("aetheus_lang", keys);
        Assert.Contains(JSInterop.Invocations, i => i.Identifier == "Aetheus.setOmniTheme" && Equals(i.Arguments[0], "light"));
    }

    /// <summary>Recette R-448: the settings page reuses OE's appearance settings, every row bound to
    /// what the site stores and applies: theme, palette and font paint their tokens (an empty theme,
    /// Essentiel as shipped, clears them), the density its band, the text and control sizes their level.</summary>
    [Fact]
    public async Task OesAppearanceSettings_ApplyEveryChoiceToTheWholeSite()
    {
        var cut = Render<SettingsPage>(p => p.Add(x => x.Tab, "appearance"));
        cut.WaitForState(() => cut.Markup.Length > 50, TimeSpan.FromSeconds(2));
        var settings = cut.FindComponent<OmniAppearanceSettings>().Instance;
        var theme = OmniThemePresets.All[1];
        var palette = OmniThemePalettes.All[0];
        var font = OmniThemeFonts.All[^1];

        Assert.True(settings.ControlSizeLevelChanged.HasDelegate);
        await cut.InvokeAsync(() => settings.PresetChanged.InvokeAsync(theme));
        await cut.InvokeAsync(() => settings.PaletteChanged.InvokeAsync(palette));
        await cut.InvokeAsync(() => settings.FontChanged.InvokeAsync(font));
        await cut.InvokeAsync(() => settings.DensityChanged.InvokeAsync(OmniDensity.Compact));
        await cut.InvokeAsync(() => settings.TextSizeLevelChanged.InvokeAsync(7));
        await cut.InvokeAsync(() => settings.ControlSizeLevelChanged.InvokeAsync(4));

        bool Stored(string key, string value) => JSInterop.Invocations.Any(i => i.Identifier == "localStorage.setItem"
            && Equals(i.Arguments[0], key) && Equals(i.Arguments[1], value));
        Assert.True(Stored("aetheus_theme_preset", theme.Name));
        Assert.True(Stored("aetheus_theme_palette", palette.Name));
        Assert.True(Stored("aetheus_theme_font", font.Name));
        Assert.True(Stored("aetheus_density", "compact"));
        Assert.True(Stored("aetheus_text_size", "7"));
        Assert.True(Stored("aetheus_control_size", "4"));
        var painted = (string?)JSInterop.Invocations.Last(i => i.Identifier == "Aetheus.setThemeTokens").Arguments[0];
        Assert.Contains("\"light\"", painted, StringComparison.Ordinal);
        Assert.Contains("\"dark\"", painted, StringComparison.Ordinal);
        Assert.Contains("--omni-font-family", painted, StringComparison.Ordinal);
        Assert.Contains(JSInterop.Invocations, i => i.Identifier == "Aetheus.setDensity" && Equals(i.Arguments[0], "compact"));
        Assert.Contains(JSInterop.Invocations, i => i.Identifier == "Aetheus.setTextSize" && Equals(i.Arguments[0], 7));
        Assert.Contains(JSInterop.Invocations, i => i.Identifier == "Aetheus.setControlSize" && Equals(i.Arguments[0], 4));

        // A new theme drops the palette and the font picked for the previous one; no theme, no tokens.
        await cut.InvokeAsync(() => settings.PresetChanged.InvokeAsync(null));
        Assert.Null(JSInterop.Invocations.Last(i => i.Identifier == "Aetheus.setThemeTokens").Arguments[0]);
    }

    /// <summary>Recette R-390 / R-448: the settings page and the Theme window read one source, so a
    /// change made in one shows in the other.</summary>
    [Fact]
    public async Task TheSettingsPageAndTheThemeWindow_ShareOneSource()
    {
        var page = Render<SettingsPage>(p => p.Add(x => x.Tab, "appearance"));
        page.WaitForState(() => page.Markup.Length > 50, TimeSpan.FromSeconds(2));
        var window = Render<Aetheus.Front.Components.Settings.SiteAppearanceWindow>();

        await page.InvokeAsync(() => page.FindComponent<OmniAppearanceSettings>().Instance.TextSizeLevelChanged.InvokeAsync(8));

        Assert.Equal(8, window.FindComponent<OmniAppearanceWindow>().Instance.TextSizeLevel);
    }
}
