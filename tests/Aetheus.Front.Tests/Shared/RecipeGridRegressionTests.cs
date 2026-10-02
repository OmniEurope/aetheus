// SPDX-License-Identifier: EUPL-1.2
using System.Globalization;
using Bunit;
using OmniEurope.Blazor.Components;

namespace Aetheus.Front.Tests.Shared;

public class RecipeGridRegressionTests : BunitContext
{
    public RecipeGridRegressionTests() => BunitTestHelper.RegisterServices(this);

    private static DateTime LocalTime => new(2026, 9, 23, 14, 0, 0, DateTimeKind.Local);

    [Fact]
    public void DateBounds_AreConvertedFromBrowserTimeToUtc()
    {
        var start = LocalTime;
        var args = new GridLoadArgs
        {
            Filters = [new("Timestamp", start.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
            OmniDataGridFilterOperator.GreaterThanOrEquals, OmniDataGridFilterOperator.LessThan,
            start.AddHours(1).ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture))]
        };
        var filter = Assert.Single(args.ToApiFilters());
        Assert.Equal(start.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture), filter.Value);
        Assert.Equal(start.AddHours(1).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture), filter.SecondValue);
    }

    [Theory]
    [InlineData(OmniDataGridFilterOperator.Equals, 1)]
    [InlineData(OmniDataGridFilterOperator.GreaterThanOrEquals, 2)]
    [InlineData(OmniDataGridFilterOperator.NotIn, 1)]
    public void NumericFilters_RespectTheirOperator(OmniDataGridFilterOperator operation, int count)
    {
        var args = new GridLoadArgs { Filters = [new("Count", "1", operation)] };
        var rows = new[] { new { Count = 1 }, new { Count = 10 } };
        Assert.Equal(count, args.ToClientPage(rows).Count);
        Assert.Equal(count, args.ClientFilteredCount(rows));
    }

    [Fact]
    public void PipelineGrid_KeepsHeadersUnderOeLoadingBar_ThenShowsEmptyState()
    {
        var cut = Render<PipelineRunsGrid>(p => p.Add(x => x.IsLoading, true));
        Assert.NotEmpty(cut.FindAll("thead th"));
        Assert.Single(cut.FindAll("thead > tr.omni-data-grid__progress .omni-loading-bar--active"));
        Assert.Empty(cut.FindAll(".aetheus-loader"));
        Assert.Empty(cut.FindAll(".empty-state"));
        cut.Render(p => p.Add(x => x.IsLoading, false));
        Assert.Empty(cut.FindAll(".omni-data-grid__progress"));
        Assert.NotEmpty(cut.FindAll(".omni-data-grid__state"));
    }
}
