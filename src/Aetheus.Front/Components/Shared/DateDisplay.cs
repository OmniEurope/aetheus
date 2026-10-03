// SPDX-License-Identifier: EUPL-1.2
using System.Globalization;

namespace Aetheus.Front.Components.Shared;

/// <summary>
/// The date and time shapes a screen shows, all in the user's culture (the one <c>index.html</c> boots
/// Blazor with, from <c>aetheus_lang</c> or the browser): a French user reads 02/10/2026 and 02/10, an
/// English one 10/2/2026 and 10/2. A hard-coded pattern such as <c>yyyy-MM-dd</c> or <c>MM-dd</c> ignores
/// that choice; standard formats (<c>"d"</c>, <c>"g"</c>, <c>"G"</c>, <c>"F"</c>) and these helpers do not.
/// Machine values (file names, filters, exports, ISO wire strings) keep the invariant culture instead.
/// </summary>
internal static class DateDisplay
{
    /// <summary>Day and month without the year, in the culture's order and separator (02/10, 10/2): chart axes.</summary>
    public static string DayMonth(DateTime value) => value.ToString(DayMonthPattern(CultureInfo.CurrentCulture), CultureInfo.CurrentCulture);

    /// <inheritdoc cref="DayMonth(DateTime)"/>
    public static string DayMonth(DateOnly value) => value.ToString(DayMonthPattern(CultureInfo.CurrentCulture), CultureInfo.CurrentCulture);

    /// <summary>Day and month, then the culture's short time: chart axes over more than a day.</summary>
    public static string DayMonthTime(DateTime value)
    {
        var culture = CultureInfo.CurrentCulture;
        return value.ToString(DayMonthPattern(culture) + " " + culture.DateTimeFormat.ShortTimePattern, culture);
    }

    /// <summary>The culture's short date with a millisecond time: log lines read for timing.</summary>
    public static string PreciseFormatString =>
        "{0:" + CultureInfo.CurrentCulture.DateTimeFormat.ShortDatePattern + " HH:mm:ss.fff}";

    /// <summary>
    /// The culture's short date pattern without its year: "dd/MM/yyyy" gives "dd/MM", "M/d/yyyy" gives
    /// "M/d", "yyyy-MM-dd" gives "MM-dd" (only where the culture itself writes dates that way).
    /// </summary>
    internal static string DayMonthPattern(CultureInfo culture)
    {
        var pattern = culture.DateTimeFormat.ShortDatePattern;
        var start = pattern.IndexOf('y', StringComparison.Ordinal);
        if (start < 0) return pattern;
        var end = pattern.LastIndexOf('y') + 1;
        // Drop the year with the separator that joins it to the rest: after it when it leads, before it otherwise.
        if (start == 0)
        {
            while (end < pattern.Length && !char.IsLetter(pattern[end])) end++;
        }
        else
        {
            while (start > 0 && !char.IsLetter(pattern[start - 1])) start--;
        }
        return pattern.Remove(start, end - start).Trim();
    }
}
