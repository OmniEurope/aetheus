// SPDX-License-Identifier: EUPL-1.2
using System.Globalization;
using Aetheus.Front.Components.Shared;

namespace Aetheus.Front.Tests.Shared;

/// <summary>
/// Dates follow the user's culture: a French user read chart axes as "09-03" (month first, recette
/// 2026-10-02) because the labels were hard-coded "MM-dd".
/// </summary>
public sealed class DateDisplayTests
{
    private static readonly DateTime Sample = new(2026, 9, 3, 14, 5, 7, 123);

    [Theory]
    [InlineData("fr-FR", "03/09")]
    [InlineData("en-US", "9/3")]
    [InlineData("de-DE", "03.09")]
    [InlineData("sv-SE", "09-03")]
    public void DayMonth_FollowsTheCultureOrderAndSeparator(string culture, string expected)
        => InCulture(culture, () => Assert.Equal(expected, DateDisplay.DayMonth(Sample)));

    [Fact]
    public void DayMonth_OfADateOnly_MatchesTheDateTime()
        => InCulture("fr-FR", () => Assert.Equal("03/09", DateDisplay.DayMonth(DateOnly.FromDateTime(Sample))));

    [Theory]
    [InlineData("fr-FR", "03/09 14:05")]
    [InlineData("en-US", "9/3 2:05 PM")]
    public void DayMonthTime_UsesTheCultureShortTime(string culture, string expected)
        => InCulture(culture, () => Assert.Equal(expected, Normalize(DateDisplay.DayMonthTime(Sample))));

    [Theory]
    [InlineData("fr-FR", "03/09/2026 14:05:07.123")]
    [InlineData("en-US", "9/3/2026 14:05:07.123")]
    public void PreciseFormatString_KeepsMillisecondsAfterTheCultureShortDate(string culture, string expected)
        => InCulture(culture, () => Assert.Equal(expected, string.Format(CultureInfo.CurrentCulture, DateDisplay.PreciseFormatString, Sample)));

    // ICU writes a narrow no-break space before AM/PM on some platforms.
    private static string Normalize(string value) => value.Replace(' ', ' ').Replace(' ', ' ');

    private static void InCulture(string name, Action assert)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(name);
            assert();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
