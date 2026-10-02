// SPDX-License-Identifier: EUPL-1.2
using System.Globalization;
using System.Text.Json;

namespace Aetheus.Front.Components.Settings;

/// <summary>
/// Recette R-232 / R-390: the theme, palette, font and scales of the whole site. Themes, palettes and
/// fonts are those of the OmniEurope.Blazor catalogue; what the site stores is their names, plus the
/// token values they resolve to, which the browser paints on the document root. No theme is OE's first
/// one, Essentiel, which the shipped stylesheet already draws (user decision 2026-09-29: no separate
/// "Aetheus" theme).
/// </summary>
internal static class SiteAppearance
{
    /// <summary>The lowest and highest level of the text size, density and control size scales; 5 is as drawn.</summary>
    public const int MinimumLevel = 1;
    public const int MaximumLevel = 10;
    public const int DefaultLevel = 5;

    /// <summary>The catalogue theme of that name, or null for the default one (Essentiel, as shipped).</summary>
    public static OmniThemePreset? Preset(string? name) => string.IsNullOrWhiteSpace(name)
        ? null
        : OmniThemePresets.All.FirstOrDefault(preset => string.Equals(preset.Name, name, StringComparison.Ordinal));

    /// <summary>The catalogue palette of that name, or null for the theme's own.</summary>
    public static OmniThemePalette? Palette(string? name) => string.IsNullOrWhiteSpace(name)
        ? null
        : OmniThemePalettes.All.FirstOrDefault(palette => string.Equals(palette.Name, name, StringComparison.Ordinal));

    /// <summary>The catalogue font of that name, or null for the one the theme is drawn with.</summary>
    public static OmniThemeFont? Font(string? name) => string.IsNullOrWhiteSpace(name)
        ? null
        : OmniThemeFonts.All.FirstOrDefault(font => string.Equals(font.Name, name, StringComparison.Ordinal));

    /// <summary>
    /// The token values of a theme, a palette and a font, both modes, as <c>{ "light": {...}, "dark": {...} }</c>;
    /// null when none is chosen, which leaves the shipped stylesheet alone. A palette without a theme
    /// changes the colours only, and a font sets the text and the headings over whatever the theme said,
    /// as OmniThemeScope does.
    /// </summary>
    public static string? TokensJson(string? presetName, string? paletteName, string? fontName = null)
    {
        var preset = Preset(presetName);
        var palette = Palette(paletteName);
        var font = Font(fontName);
        var (light, dark) = (preset, palette) switch
        {
            ({ } theme, null) => (theme.Light, theme.Dark),
            ({ } theme, { } colours) => (theme.With(colours).Light, theme.With(colours).Dark),
            (null, { } colours) => (colours.Light, colours.Dark),
            _ => ((IReadOnlyDictionary<string, string>?)null, (IReadOnlyDictionary<string, string>?)null)
        };
        if (font is not null)
        {
            light = WithFont(light, font);
            dark = WithFont(dark, font);
        }

        return light is null || dark is null ? null : JsonSerializer.Serialize(new { light, dark });
    }

    private static Dictionary<string, string> WithFont(IReadOnlyDictionary<string, string>? tokens, OmniThemeFont font)
    {
        var merged = tokens is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(tokens, StringComparer.Ordinal);
        merged["--omni-font-family"] = font.Family;
        merged["--omni-heading-font-family"] = font.Family;
        return merged;
    }

    /// <summary>The stored density; anything unknown reads as the shipped one.</summary>
    public static OmniDensity ParseDensity(string? stored) => stored switch
    {
        "compact" => OmniDensity.Compact,
        "spacious" => OmniDensity.Spacious,
        _ => OmniDensity.Comfortable
    };

    public static string StoredDensity(OmniDensity density) => density switch
    {
        OmniDensity.Compact => "compact",
        OmniDensity.Spacious => "spacious",
        _ => "comfortable"
    };

    /// <summary>A stored text size or control size level; anything missing or out of range is as drawn.</summary>
    public static int ParseLevel(string? stored) =>
        int.TryParse(stored, NumberStyles.None, CultureInfo.InvariantCulture, out var level)
        && level is >= MinimumLevel and <= MaximumLevel ? level : DefaultLevel;

    public static string StoredLevel(int level) =>
        Math.Clamp(level, MinimumLevel, MaximumLevel).ToString(CultureInfo.InvariantCulture);
}
