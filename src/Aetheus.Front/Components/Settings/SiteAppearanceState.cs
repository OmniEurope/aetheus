// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Settings;

/// <summary>
/// Recette R-390 / R-448: the one source of the site's look, shared by the settings page
/// (<c>OmniAppearanceSettings</c>) and the user menu's Theme window (<c>OmniAppearanceWindow</c>). It reads
/// the choices stored in this browser, and on each change stores the value and applies it on the document
/// root at once: the mode (<c>data-omni-theme</c>), the theme, palette and font tokens, the density
/// (<c>data-omni-density</c>), the text size (<c>data-oe-text-size</c>) and the control size
/// (<c>data-oe-control-size</c>). The boot script of index.html paints the same keys before the first frame.
/// </summary>
public sealed class SiteAppearanceState(IJSRuntime js)
{
    /// <summary>Raised after any change, so the other surface showing the same values redraws.</summary>
    public event Action? Changed;

    public OmniAppearance Appearance { get; private set; } = OmniAppearance.Dark;
    public OmniThemePreset? Preset { get; private set; }
    public OmniThemePalette? Palette { get; private set; }
    public OmniThemeFont? Font { get; private set; }
    public OmniDensity Density { get; private set; } = OmniDensity.Comfortable;
    public int TextSizeLevel { get; private set; } = SiteAppearance.DefaultLevel;
    public int ControlSizeLevel { get; private set; } = SiteAppearance.DefaultLevel;

    /// <summary>Reads the choices stored in this browser; called by each surface as it opens.</summary>
    public async Task LoadAsync()
    {
        Appearance = ParseAppearance(await ReadAsync(StorageKeys.Theme));
        Preset = SiteAppearance.Preset(await ReadAsync(StorageKeys.ThemePreset));
        Palette = SiteAppearance.Palette(await ReadAsync(StorageKeys.ThemePalette));
        Font = SiteAppearance.Font(await ReadAsync(StorageKeys.ThemeFont));
        Density = SiteAppearance.ParseDensity(await ReadAsync(StorageKeys.Density));
        TextSizeLevel = SiteAppearance.ParseLevel(await ReadAsync(StorageKeys.TextSize));
        ControlSizeLevel = SiteAppearance.ParseLevel(await ReadAsync(StorageKeys.ControlSize));
        Changed?.Invoke();
    }

    public async Task SetAppearanceAsync(OmniAppearance appearance)
    {
        Appearance = appearance;
        var stored = StoredAppearance(appearance);
        await js.InvokeVoidAsync("localStorage.setItem", StorageKeys.Theme, stored);
        await js.InvokeVoidAsync("Aetheus.setOmniTheme", stored);
        Changed?.Invoke();
    }

    /// <summary>A new theme comes with its own palette and font: the ones picked for the previous theme
    /// are dropped, as OmniAppearanceSettings does.</summary>
    public async Task SetPresetAsync(OmniThemePreset? preset)
    {
        Preset = preset;
        Palette = null;
        Font = null;
        await ApplyTokensAsync();
    }

    public async Task SetPaletteAsync(OmniThemePalette? palette)
    {
        Palette = palette;
        await ApplyTokensAsync();
    }

    public async Task SetFontAsync(OmniThemeFont? font)
    {
        Font = font;
        await ApplyTokensAsync();
    }

    public async Task SetDensityAsync(OmniDensity density)
    {
        Density = density;
        var stored = SiteAppearance.StoredDensity(density);
        await js.InvokeVoidAsync("localStorage.setItem", StorageKeys.Density, stored);
        await js.InvokeVoidAsync("Aetheus.setDensity", stored);
        Changed?.Invoke();
    }

    public async Task SetTextSizeLevelAsync(int level)
    {
        TextSizeLevel = Math.Clamp(level, SiteAppearance.MinimumLevel, SiteAppearance.MaximumLevel);
        await js.InvokeVoidAsync("localStorage.setItem", StorageKeys.TextSize, SiteAppearance.StoredLevel(TextSizeLevel));
        await js.InvokeVoidAsync("Aetheus.setTextSize", TextSizeLevel);
        Changed?.Invoke();
    }

    public async Task SetControlSizeLevelAsync(int level)
    {
        ControlSizeLevel = Math.Clamp(level, SiteAppearance.MinimumLevel, SiteAppearance.MaximumLevel);
        await js.InvokeVoidAsync("localStorage.setItem", StorageKeys.ControlSize, SiteAppearance.StoredLevel(ControlSizeLevel));
        await js.InvokeVoidAsync("Aetheus.setControlSize", ControlSizeLevel);
        Changed?.Invoke();
    }

    /// <summary>Stores the theme, palette and font and the token values they resolve to, then repaints.</summary>
    private async Task ApplyTokensAsync()
    {
        var presetName = Preset?.Name ?? string.Empty;
        var paletteName = Palette?.Name ?? string.Empty;
        var fontName = Font?.Name ?? string.Empty;
        await js.InvokeVoidAsync("localStorage.setItem", StorageKeys.ThemePreset, presetName);
        await js.InvokeVoidAsync("localStorage.setItem", StorageKeys.ThemePalette, paletteName);
        await js.InvokeVoidAsync("localStorage.setItem", StorageKeys.ThemeFont, fontName);
        await js.InvokeVoidAsync("Aetheus.setThemeTokens", SiteAppearance.TokensJson(presetName, paletteName, fontName));
        Changed?.Invoke();
    }

    private ValueTask<string?> ReadAsync(string key) => js.InvokeAsync<string?>("localStorage.getItem", key);

    /// <summary>
    /// The stored value is the preference, not the painted mode: <c>system</c> is resolved by the
    /// browser on each paint. An unknown or absent value reads as dark, the application's default
    /// before the System option existed.
    /// </summary>
    internal static OmniAppearance ParseAppearance(string? stored) => stored switch
    {
        "light" => OmniAppearance.Light,
        "system" => OmniAppearance.System,
        _ => OmniAppearance.Dark
    };

    internal static string StoredAppearance(OmniAppearance appearance) => appearance switch
    {
        OmniAppearance.Light => "light",
        OmniAppearance.System => "system",
        _ => "dark"
    };
}
